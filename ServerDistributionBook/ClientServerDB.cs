using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MySql.Data.MySqlClient;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SiteBook;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ServerDistributionBook
{
    /// <summary>
    /// Рассширенный класс клиента
    /// </summary>
    [BsonIgnoreExtraElements]
    class ClientServerDB : ClientSDB
    {
        /// <summary>
        /// Очередь книг на чтение
        /// </summary>
        public Queue<string> queueBook { get; set; }
        /// <summary>
        /// Соединение с RabbitMQ
        /// </summary>
        [BsonIgnore]
        IConnection Conn;
        /// <summary>
        /// Дата освобождения от чтения
        /// </summary>
        public DateTime TimeRead { get; set; }
        /// <summary>
        /// Дата освобождения от активной книги
        /// </summary>
        public DateTime TimeReadActive { get; set; }
        /// <summary>
        /// Поток на получение и чтение книги
        /// </summary>
        [BsonIgnore]
        public Thread readingBook;
        /// <summary>
        /// Мьютекс выполнение объединенных операций
        /// </summary>
        [BsonIgnore]
        Mutex mutexSend;
        /// <summary>
        /// Коллекция MongoDB
        /// </summary>
        [BsonIgnore]
        IMongoCollection<ClientServerDB> CollectionMongo;
        /// <summary>
        /// Индикатор перезагрузки объекта
        /// </summary>
        [BsonIgnore]
        bool ReloadObject;
        /// <summary>
        /// Строка подключения MySQL
        /// </summary>
        [BsonIgnore]
        string ConnectionString;

        public ClientServerDB()
        { }
        /// <summary>
        /// Конструктор рссширеного объекта клиента
        /// </summary>
        /// <param name="clientSDB">Объект клиента </param>
        /// <param name="conn">Соединение с RabbitMQ</param>
        /// <param name="collection">Коллекция MongoDB</param>
        /// <param name="connectionString">Строка подключения MySQL</param>
        public ClientServerDB(ClientSDB clientSDB, IConnection conn, IMongoCollection<ClientServerDB> collection, string connectionString)
        {
            Surname = clientSDB.Surname;
            Name = clientSDB.Name;
            Address = clientSDB.Address;
            PagesPerDay = clientSDB.PagesPerDay;
            ReadingIntervalActive = clientSDB.ReadingIntervalActive;
            ReadingIntervalPassive = clientSDB.ReadingIntervalPassive;
            LevelLanguages = clientSDB.LevelLanguages;
            DataRegistration = clientSDB.DataRegistration;
            queueBook = new Queue<string>();
            TimeReadActive = DateTime.Now;
            TimeRead = DateTime.Now;
            ReloadObject = false;

            InitComponent(conn, collection, connectionString);
            RedistributionUnallocatedBook();
        }
        /// <summary>
        /// Инициализация компонентов при перезагрузке объекта
        /// </summary>
        /// <param name="conn">Соединение с RabbitMQ</param>
        /// <param name="collection">Коллекция MongoDB</param>
        /// <param name="connectionString">Строка подключения MySQL</param>
        /// <param name="timeReload">Расчетное время освобождения от чтения</param>
        internal void InitComponentReload(IConnection conn, IMongoCollection<ClientServerDB> collection, string connectionString, DateTime timeReload)
        {
            TimeRead = timeReload;
            ReloadObject = true;
            InitComponent(conn, collection, connectionString);
        }
        /// <summary>
        /// Инициализация компонентов
        /// </summary>
        /// <param name="conn">Соединение с RabbitMQ</param>
        /// <param name="collection">Коллекция MongoDB</param>
        /// <param name="connectionString">Строка подключения MySQL</param>
        private void InitComponent(IConnection conn, IMongoCollection<ClientServerDB> collection, string connectionString)
        {
            Conn = conn;
            CollectionMongo = collection;
            ConnectionString = connectionString;
            //определения потока и задание ему метода получения и чтения книг
            readingBook = new Thread(new ThreadStart(ReadingBook));
            mutexSend = new Mutex();
        }
        /// <summary>
        /// Перевод книг из очереди нераспределённых книг в очередь распределения
        /// </summary>
        private void RedistributionUnallocatedBook()
        {
            IModel ChannelAdding = Conn.CreateModel();
            ChannelAdding.QueueBind("BookQueue", "ServerDB", "Book", null);

            while (true)
            {
                BasicGetResult result = ChannelAdding.BasicGet("UnallocatedBookQueue", false);
                if (result == null)
                {
                    break;
                }
                else
                {
                    ChannelAdding.BasicPublish("ServerDB", "Book", null, result.Body);
                    ChannelAdding.BasicAck(result.DeliveryTag, false);
                }
            }
        }

        /// <summary>
        /// Метод получения и чтения книг
        /// </summary>
        public void ReadingBook()
        {
            //проверка на перезагрузку объекта и ожидание прочтения текущей книги при необходимости
            if (ReloadObject)
            {
                ReloadObject = false;
                DateTime DTNow;
                if (TimeReadActive > DateTime.Now)
                {
                    TimeSpan timeSleepActive = TimeReadActive - DateTime.Now;
                    Thread.Sleep(timeSleepActive);
                    DTNow = DateTime.Now;
                }
                else
                {
                    DTNow = TimeReadActive;
                }
                //занесение даты прочтения книги в MySQL
                string sqlExpressionReload = "UPDATE books SET DataReading = @DTNow WHERE AddressClient = @AddressClient AND DataReading IS NULL";
                using (MySqlConnection ConnMySQL = new MySqlConnection(ConnectionString))
                {
                    ConnMySQL.Open();
                    MySqlCommand commandReload = new MySqlCommand(sqlExpressionReload, ConnMySQL);
                    MySqlParameter AddressClientBookParam = new MySqlParameter("@AddressClient", Address);
                    commandReload.Parameters.Add(AddressClientBookParam);
                    MySqlParameter DataReadingBookParam = new MySqlParameter("@DTNow", DTNow);
                    commandReload.Parameters.Add(DataReadingBookParam);
                    commandReload.ExecuteNonQuery();
                }
            }
            while (true)
            {
                if (queueBook.Count != 0)
                {
                    mutexSend.WaitOne();
                    //получение книги из очереди и дессериализация
                    string book = queueBook.Dequeue();
                    BookSDB bookSDB = JsonSerializer.Deserialize<BookSDB>(book);
                    //подсчет времени ожидания и даты окончания чтения книги
                    double timeSleep = TimeReadingBook(bookSDB.Language, bookSDB.Pages, DateTime.Now);
                    DateTime timeReadActive = DateTime.Now.AddSeconds(timeSleep);
                    SendEmail(Address, bookSDB);

                    //Занесение книги в MySQL
                    DateTime DataGetting = DateTime.Now;
                    string sqlExpression = "insert_book";
                    object idBook;
                    using (MySqlConnection ConnMySQL = new MySqlConnection(ConnectionString))
                    {
                        ConnMySQL.Open();
                        MySqlCommand command = new MySqlCommand(sqlExpression, ConnMySQL);
                        command.CommandType = CommandType.StoredProcedure;
                        MySqlParameter AddressClientParam = new MySqlParameter("@AddressClientP", Address);
                        command.Parameters.Add(AddressClientParam);
                        MySqlParameter LanguageParam = new MySqlParameter("@LanguageP", (int)bookSDB.Language);
                        command.Parameters.Add(LanguageParam);
                        MySqlParameter NameParam = new MySqlParameter("@NameP", bookSDB.Name);
                        command.Parameters.Add(NameParam);
                        MySqlParameter PagesParam = new MySqlParameter("@PagesP", bookSDB.Pages);
                        command.Parameters.Add(PagesParam);
                        MySqlParameter DataGettingParam = new MySqlParameter("@DataGettingP", DataGetting);
                        command.Parameters.Add(DataGettingParam);
                        idBook = command.ExecuteScalar();
                    }

                    //занесение книги в MongoDB
                    var filter = Builders<ClientServerDB>.Filter.Eq("Address", Address);
                    var updateTimeReadActive = Builders<ClientServerDB>.Update.Set(x => x.TimeReadActive, timeReadActive.AddHours(3));
                    CollectionMongo.UpdateOne(filter, updateTimeReadActive);
                    var updateBook = Builders<ClientServerDB>.Update.Pull(x => x.queueBook, book);
                    CollectionMongo.UpdateOne(filter, updateBook);

                    mutexSend.ReleaseMutex();
                    //чтение книги
                    Thread.Sleep((int)timeSleep * 1000);
                    //занесение даты прочтения книги в MySQL
                    DateTime DataReading = DateTime.Now;
                    using (MySqlConnection ConnMySQL = new MySqlConnection(ConnectionString))
                    {
                        ConnMySQL.Open();
                        MySqlCommand commandReading = new MySqlCommand();
                        commandReading.Connection = ConnMySQL;
                        commandReading.CommandText = "UPDATE books SET DataReading = @DataReading WHERE Id = @Id";
                        MySqlParameter DataReadingParam = new MySqlParameter("@DataReading", DataReading);
                        commandReading.Parameters.Add(DataReadingParam);
                        MySqlParameter idParam = new MySqlParameter("@Id", idBook);
                        commandReading.Parameters.Add(idParam);
                        commandReading.ExecuteNonQuery();
                    }
                }
                else break;
            }
        }

        /// <summary>
        /// Отписка клиента и перенос книг из его очереди чтения в очередь распределения
        /// </summary>
        public void Unsubscribe()
        {
            //остановка потока чтения
            if (readingBook.ThreadState == ThreadState.Running)
            {
                mutexSend.WaitOne();
                readingBook.Abort();
            }

            //перенос книг из его очереди чтения в очередь распределения
            IModel ChannelDeleted = Conn.CreateModel();
            ChannelDeleted.QueueBind("BookQueue", "ServerDB", "Book", null);

            while (true)
            {
                if (queueBook.Count != 0)
                {
                    string book = queueBook.Dequeue();
                    byte[] messageBodyBytes = Encoding.UTF8.GetBytes(book);
                    ChannelDeleted.BasicPublish("ServerDB", "Book", null, messageBodyBytes);
                }
                else break;
            }
        }

        /// <summary>
        /// Создание рассширеного объекта клиента для записи в MongoDB (корректировка времени)
        /// </summary>
        /// <returns></returns>
        public ClientServerDB ConvertMongoDate()
        {
            ClientServerDB clientserverDBMongo = new ClientServerDB();
            clientserverDBMongo.Surname = Surname;
            clientserverDBMongo.Name = Name;
            clientserverDBMongo.Address = Address;
            clientserverDBMongo.PagesPerDay = PagesPerDay;
            clientserverDBMongo.ReadingIntervalActive = ReadingIntervalActive;
            clientserverDBMongo.ReadingIntervalPassive = ReadingIntervalPassive;
            clientserverDBMongo.LevelLanguages = LevelLanguages;
            clientserverDBMongo.DataRegistration = DataRegistration.AddHours(3);
            clientserverDBMongo.queueBook = queueBook;
            clientserverDBMongo.TimeReadActive = TimeReadActive.AddHours(3);
            clientserverDBMongo.TimeRead = TimeRead.AddHours(3);
            return clientserverDBMongo;
        }

        /// <summary>
        /// Отправка книги по электронной почте
        /// </summary>
        /// <param name="address">wmail адресс</param>
        /// <param name="bookSDB">книга</param>
        private void SendEmail(string address, BookSDB bookSDB)
        {
        }

        /// <summary>
        /// Расчет времени чтения книги
        /// </summary>
        /// <param name="language">Язык книги</param>
        /// <param name="pages">Количество страниц книги</param>
        /// <param name="dataTime">Дата начала чтения книги</param>
        /// <returns></returns>
        public double TimeReadingBook(BookSDB.languageEnum language, int pages, DateTime dataTime)
        {
            double timeReadingBook;                                                                         //время чтения книги
            int level = 0;                                                                                  //уровень владения языком
            foreach (LevelLanguageSDB lela in LevelLanguages)
            {
                if (lela.Language == language)
                {
                    level = lela.Level;
                    break;
                }
            }
            if (level == 0)
                throw new Exception("Клиент не владеет языком" + language);
            TimeSpan interval = dataTime - DataRegistration;                                                    //интервал времени от данного до времени регистрации
            int ReadingIntervalActiveSecond = ReadingIntervalActive * ProgramServer.DaysSecond;                 //время активного чтения в цикле
            int ReadingIntervalPassiveSecond = ReadingIntervalPassive * ProgramServer.DaysSecond;               //время пассивного чтения чтения в цикле
            int cycleReading = ReadingIntervalActiveSecond + ReadingIntervalPassiveSecond;                      //время цикла чтения
            double levelPagesPerCycle = ReadingIntervalActive * PagesPerDay * (level / 10.0);                   //количество читаемых страниц в цикл
            double balanseInterval = (double)interval.TotalSeconds % (double)cycleReading;                      //текущее время нового цикла
            double remainingActiveCycle = ReadingIntervalActiveSecond - balanseInterval;                        //оставшееся время цикла активного чтения
            double timeReadingBookNCCycle = (double)(pages % levelPagesPerCycle) / (double)levelPagesPerCycle   //время чтения неполного цикла
                * (double)ReadingIntervalActiveSecond;
            int timeReadingBookFullCycle = (int)(pages / levelPagesPerCycle) * cycleReading;                    //время чтения полных циклов

            if (balanseInterval >= ReadingIntervalActiveSecond)
            {
                timeReadingBook = timeReadingBookFullCycle + timeReadingBookNCCycle + (cycleReading - balanseInterval);
            }
            else
            {
                if (remainingActiveCycle > timeReadingBookNCCycle)
                    timeReadingBook = timeReadingBookFullCycle + timeReadingBookNCCycle;
                else
                    timeReadingBook = timeReadingBookFullCycle + timeReadingBookNCCycle + ReadingIntervalPassiveSecond;
            }
            return timeReadingBook;
        }
    }
}
