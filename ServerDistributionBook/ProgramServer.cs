using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.MessagePatterns;
using RabbitMQ.Client.Events;
using System.Threading;
using SiteBook;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using MySql.Data.MySqlClient;

namespace ServerDistributionBook
{
    class ProgramServer
    {
        public static int DaysSecond;                                                                           //секундный эквивалент дня
        public static Dictionary<string, ClientServerDB> listClient = new Dictionary<string, ClientServerDB>(); //список объектов клиентов
        public static Mutex mutexDAD = new Mutex();                                                             //мьютекс, позволяющий работать только одному методу(добавления клиентов или распределения книг)

        static void Main(string[] args)
        {
            //объявление каналов связи с rabbitMQ
            ConnectionFactory factory = new ConnectionFactory
            {
                UserName = "guest",
                Password = "guest",
                VirtualHost = "/",
                HostName = "localhost",
            };
            IConnection conn = null;
            IModel channelBook = null;
            IModel channelClient = null;

            //объявление соединения с mongodb
            string connectionStringMongo = "mongodb://localhost:27017";
            MongoClient clientMongo = null;
            IMongoDatabase databaseMongo = null;
            IMongoCollection<ClientServerDB> collection = null;

            //строка соединения с MySQL
            string connectionString = "Server=127.0.0.1;Database=systemdistributionbook;port=3306;User Id=root;password=n1477353";

            try
            {
                //задание секундного эквивалента дня                                                                   
                while (true)
                {
                    Console.WriteLine("Введите секундный эквивалент дня");
                    if (Int32.TryParse(Console.ReadLine(), out DaysSecond) && DaysSecond >= 0)
                        break;
                    else
                        Console.WriteLine("Не верно введено значение");
                }

                //проверка связи с MySQL
                using (MySqlConnection connMySQL = new MySqlConnection(connectionString))
                {
                    connMySQL.Open();
                }

                //создание канала связи с rabbitMQ
                conn = factory.CreateConnection();
                channelBook = conn.CreateModel();
                channelClient = conn.CreateModel();
                channelBook.ExchangeDeclare("ServerDB", ExchangeType.Direct);
                channelBook.QueueDeclare("UnallocatedBookQueue", true, false, false, null);                       //очередь не распределенных книг
                channelBook.QueueBind("UnallocatedBookQueue", "ServerDB", "UnallocatedBook", null);
             
                //подключение к mongodb
                clientMongo = new MongoClient(connectionStringMongo);
                databaseMongo = clientMongo.GetDatabase("SystemDistributionBook");
                databaseMongo.RunCommand((Command<BsonDocument>)"{ping:1}");
                collection = databaseMongo.GetCollection<ClientServerDB>("SDBCollection");

                Console.WriteLine("Запущено");

                //проверка на перезагрузку объектов
                if (collection.CountDocuments(new BsonDocument()) != 0)
                    ObjectOverload(conn, collection, connectionString);

                //прослушивание очереди клиентов и книг
                var consumerClient = new EventingBasicConsumer(channelClient);
                consumerClient.Received += (sender, e) => AddingDeletedClient(e, channelClient, conn, collection, connectionString);

                var consumerBook = new EventingBasicConsumer(channelBook);
                consumerBook.Received += (sender, e) => DistributionBook(e, channelBook, collection);

                channelClient.BasicConsume("ClientQueue", false, consumerClient);
                channelBook.BasicConsume("BookQueue", false, consumerBook);


                Console.ReadLine();
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                if (conn != null) conn.Close();
                if (channelClient != null) channelClient.Close();
                if (channelBook != null) channelBook.Close(); 
            }
           
        }
        /// <summary>
        /// Перезагрузка объектов клиентов
        /// </summary>
        /// <param name="conn">Соединение с RabbitMQ</param>
        /// <param name="collection">Коллекция MongoDB</param>
        /// <param name="connectionString">Строка подключения MySQL</param>
        private static void ObjectOverload(IConnection conn, IMongoCollection<ClientServerDB> collection, string connectionString)
        {
            //извлечение всех объектов из MongoDB
            var filter = new BsonDocument();
            var people = collection.Find(filter).ToList();

            foreach (ClientServerDB clientServerDB in people)
            {
                //количество книг в очереди
                int queueBookCount = clientServerDB.queueBook.Count;
                //расчет начала чтения книг из очереди
                DateTime timeReadIntermediate = clientServerDB.TimeReadActive;
                if (timeReadIntermediate < DateTime.Now)
                    timeReadIntermediate = DateTime.Now;
                //перерасчет даты освобождения от чтения
                for (int i = 0; i < queueBookCount; i++)
                {
                    string book = clientServerDB.queueBook.Dequeue();
                    clientServerDB.queueBook.Enqueue(book);
                    BookSDB bookSDB = JsonSerializer.Deserialize<BookSDB>(book);
                    double timeSleep = clientServerDB.TimeReadingBook(bookSDB.Language, bookSDB.Pages, timeReadIntermediate);
                    timeReadIntermediate = timeReadIntermediate.AddSeconds(timeSleep);
                }
                clientServerDB.InitComponentReload(conn, collection, connectionString, timeReadIntermediate);

                //запись даты освобождения от чтения в MongoDB
                var filterDB = Builders<ClientServerDB>.Filter.Eq("Address", clientServerDB.Address);
                var updateTimeRead = Builders<ClientServerDB>.Update.Set(x => x.TimeRead, clientServerDB.TimeRead.AddHours(3));
                collection.UpdateOne(filterDB, updateTimeRead);

                //добавления объекта в список и запуск потока получения и чтения книг
                listClient.Add(clientServerDB.Address, clientServerDB);
                clientServerDB.readingBook.Start();
            }
        }

        /// <summary>
        /// Распределение книг
        /// </summary>
        /// <param name="e">Объект сообщения</param>
        /// <param name="channel">Канал связи с rabbitmq</param>
        /// <param name="collection">Коллекция MongoDB</param>
        private static void DistributionBook(BasicDeliverEventArgs e, IModel channel, IMongoCollection<ClientServerDB> collection)
        {
            mutexDAD.WaitOne();
            //получение и дессериализация книги
            var body = e.Body;
            var message = Encoding.UTF8.GetString(body);
            BookSDB book = JsonSerializer.Deserialize<BookSDB>(message);

            ClientServerDB clientSDB = null;
            DateTime timeRead = new DateTime();                     //промежуточное время окончания чтения книги
            DateTime timeReadMain = new DateTime();                 //время оканчания чтения книги
            DateTime timeReadNow = new DateTime();                  //время начала чтения книги
            bool first = true;
            foreach (var client in listClient)
            {
                foreach (LevelLanguageSDB lela in client.Value.LevelLanguages)
                {
                    //проверка клиента на знание языка
                    if (book.Language == lela.Language)
                    {
                        //определение времени начала чтения книги
                        if (client.Value.TimeRead > DateTime.Now) timeReadNow = client.Value.TimeRead;
                        else timeReadNow = DateTime.Now;

                        //промежуточное определение времени окончания чтения книги
                        timeRead = timeReadNow.AddSeconds(client.Value.TimeReadingBook(book.Language, book.Pages, timeReadNow));
                        if (first || timeRead < timeReadMain)
                        {
                            //если клиент прочитает книгу быстрее предыдущего клиента, он становится активным клиентом
                            if (first) first = false;
                            clientSDB = client.Value;
                            timeReadMain = timeRead;
                        }
                    }
                }
            }
            //если подходящих клиентов не найденоб отправка книги в очередь не распределенных книг
            if (clientSDB == null)
            {
                channel.BasicPublish("ServerDB", "UnallocatedBook", null, e.Body);
                Console.WriteLine("UnallocatedBook" + " " + message);
            }
            else
            {
                //изменение времени оканчания чтения клиента и добавление книги ему в очередь
                clientSDB.TimeRead = timeReadMain;
                clientSDB.queueBook.Enqueue(message);

                //изменение времени оканчания чтения клиента и добавление книги в MongoDB 
                var filter = Builders<ClientServerDB>.Filter.Eq("Address", clientSDB.Address);
                var updateTimeRead = Builders<ClientServerDB>.Update.Set(x => x.TimeRead, clientSDB.TimeRead.AddHours(3));
                collection.UpdateOne(filter, updateTimeRead);
                var updateBook = Builders<ClientServerDB>.Update.AddToSet(x => x.queueBook, message);
                collection.UpdateOne(filter, updateBook);

                //если клиент в данный момент не читает и у него в очереди нет книг, запуск потока получения и чтения книг
                if (clientSDB.readingBook.ThreadState == ThreadState.Unstarted || clientSDB.readingBook.ThreadState == ThreadState.Stopped)
                {
                    if (clientSDB.readingBook.ThreadState == ThreadState.Stopped)
                        clientSDB.readingBook = new Thread(new ThreadStart(clientSDB.ReadingBook));
                    clientSDB.readingBook.Start();
                }

                Console.WriteLine(clientSDB.Address + " " + message + clientSDB.TimeRead);
            }
            channel.BasicAck(e.DeliveryTag, false);
            mutexDAD.ReleaseMutex();          
        }

        /// <summary>
        /// Добавление, удаление клиентов
        /// </summary>
        /// <param name="e">Объект сообщения</param>
        /// <param name="channel">Канал связи с RabbitMQ</param>
        /// <param name="conn">Соединение с RabbitMQ</param>
        /// <param name="collection">Коллекция MongoDB</param>
        /// <param name="connectionString">Строка подключения MySQL</param> 
        private static void AddingDeletedClient(BasicDeliverEventArgs e, IModel channel, IConnection conn, IMongoCollection<ClientServerDB> collection, string connectionString)
        {
            mutexDAD.WaitOne();
            //получение и распределение операций (добавление или отписка клиента)
            var body = e.Body;
            var bodytype = e.BasicProperties.Type;
            var message = Encoding.UTF8.GetString(body);
            //если добавление клиента
            if (bodytype == "Adding")
            {
                //сериализация клиента
                ClientSDB clientSDB = JsonSerializer.Deserialize<ClientSDB>(message);

                //проверка клиента на наличие в базе
                if (!listClient.ContainsKey(clientSDB.Address))
                {
                    //проверка в MySQL был ли клиент раннее подписан
                    string sqlcommand = "SELECT Address FROM clients WHERE Address = @Address";
                    bool HasRows;
                    using (MySqlConnection connMySQL = new MySqlConnection(connectionString))
                    {
                        connMySQL.Open();
                        MySqlCommand commandchek = new MySqlCommand(sqlcommand, connMySQL);
                        MySqlParameter AddressCheckParam = new MySqlParameter("@Address", clientSDB.Address);
                        commandchek.Parameters.Add(AddressCheckParam);
                        MySqlDataReader reader = commandchek.ExecuteReader();
                        HasRows = reader.HasRows;
                    }
                    if (!HasRows)
                    {
                        //создание рассширеного объекта клиента и добавление его в список
                        ClientServerDB clientServerDB = new ClientServerDB(clientSDB, conn, collection, connectionString);
                        listClient.Add(clientServerDB.Address, clientServerDB);
                        //добавление клиента в MongoDB
                        ClientServerDB clientServerDBMongo = clientServerDB.ConvertMongoDate();
                        collection.InsertOne(clientServerDBMongo);
                        //добавление клиента в MySQL
                        using (MySqlConnection connMySQL = new MySqlConnection(connectionString))
                        {
                            connMySQL.Open();
                            MySqlCommand command = new MySqlCommand();
                            command.Connection = connMySQL;
                            command.CommandText = "INSERT INTO clients (Address, Surname, Name, PagesPerDay, ReadingIntervalActive, " +
                                "ReadingIntervalPassive, DataRegistration, Subscription) " +
                                "VALUES (@Addres, @Surname, @Name, @PagesPerDay, @ReadingIntervalActive, @ReadingIntervalPassive, @DataRegistration, @Subscription)";
                            MySqlParameter AddressParam = new MySqlParameter("@Addres", clientServerDB.Address);
                            command.Parameters.Add(AddressParam);
                            MySqlParameter SurnameParam = new MySqlParameter("@Surname", clientServerDB.Surname);
                            command.Parameters.Add(SurnameParam);
                            MySqlParameter NameParam = new MySqlParameter("@Name", clientServerDB.Name);
                            command.Parameters.Add(NameParam);
                            MySqlParameter PagesPerDayParam = new MySqlParameter("@PagesPerDay", clientServerDB.PagesPerDay);
                            command.Parameters.Add(PagesPerDayParam);
                            MySqlParameter ReadingIntervalActiveParam = new MySqlParameter("@ReadingIntervalActive", clientServerDB.ReadingIntervalActive);
                            command.Parameters.Add(ReadingIntervalActiveParam);
                            MySqlParameter ReadingIntervalPassiveParam = new MySqlParameter("@ReadingIntervalPassive", clientServerDB.ReadingIntervalPassive);
                            command.Parameters.Add(ReadingIntervalPassiveParam);
                            MySqlParameter DataRegistrationParam = new MySqlParameter("@DataRegistration", clientServerDB.DataRegistration);
                            command.Parameters.Add(DataRegistrationParam);
                            MySqlParameter SubscriptionParam = new MySqlParameter("@Subscription", "Подписан");
                            command.Parameters.Add(SubscriptionParam);
                            command.ExecuteNonQuery();
                            foreach (LevelLanguageSDB ll in clientServerDB.LevelLanguages)
                            {
                                MySqlCommand commandll = new MySqlCommand();
                                commandll.Connection = connMySQL;
                                commandll.CommandText = "INSERT INTO levellanguages (AddressClient, Language, Level) VALUES (@AddressClient, @Language, @Level)";
                                MySqlParameter AddressClientParam = new MySqlParameter("@AddressClient", clientServerDB.Address);
                                commandll.Parameters.Add(AddressClientParam);
                                MySqlParameter LanguageParam = new MySqlParameter("@Language", (int)ll.Language);
                                commandll.Parameters.Add(LanguageParam);
                                MySqlParameter LevelParam = new MySqlParameter("@Level", ll.Level);
                                commandll.Parameters.Add(LevelParam);
                                commandll.ExecuteNonQuery();
                            }
                        }
                    }
                }
            }
            //если отписка клиента
            else if (bodytype == "Deleted")
            {
                //проверка клиента на наличие в базе
                if (listClient.ContainsKey(message))
                {
                    //отписка клиента и удаление его из списка
                    listClient[message].Unsubscribe();
                    listClient.Remove(message);
                    //удаление клиента из MongoDB
                    collection.DeleteOne(p => p.Address == message);
                    //изменение статуса клиента на "Отписан" в MySQL
                    using (MySqlConnection connMySQL = new MySqlConnection(connectionString))
                    {
                        connMySQL.Open();
                        MySqlCommand command = new MySqlCommand();
                        command.Connection = connMySQL;
                        command.CommandText = "UPDATE clients SET Subscription = 'Отписан' WHERE Address = @Address";
                        MySqlParameter AddressParam = new MySqlParameter("@Address", message);
                        command.Parameters.Add(AddressParam);
                        command.ExecuteNonQuery();
                    }
                }
            }
            channel.BasicAck(e.DeliveryTag, false);
            mutexDAD.ReleaseMutex();
        }
    }
}
