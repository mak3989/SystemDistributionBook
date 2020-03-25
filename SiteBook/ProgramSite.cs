using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.RegularExpressions;
using RabbitMQ.Client;
using RabbitMQ.Client.MessagePatterns;
using RabbitMQ.Client.Events;

namespace SiteBook
{
    class ProgramSite
    {
        static void Main(string[] args)
        {
            //отменяющий токен
            CancellationTokenSource cancelTokenSource = null;
            CancellationToken token;

            //объявление канала связи с rabbitMQ
            string exchangeName = "SiteExchange";
            string queueName1 = "BookQueue";                        //очередь распределения книг
            string routingKey1 = "Book";
            string queueName2 = "ClientQueue";                      //очередь добавления/удаления клиентов
            string routingKey2 = "Client";
            ConnectionFactory factory = new ConnectionFactory
            {
                UserName = "guest",
                Password = "guest",
                VirtualHost = "/",
                HostName = "localhost"
            };
            IConnection conn = null;
            IModel channel = null;

            //команды контектного ввода
            string AddingBooks = "1 - Запуск добавления книг";
            string AddingClient = "2 - Добавление клиента";
            string DeletedClient = "3 - Удаление клиента";

            try
            {
                //создание канала связи с rabbitMQ
                conn = factory.CreateConnection();
                channel = conn.CreateModel();
                channel.ExchangeDeclare(exchangeName, ExchangeType.Direct);
                channel.QueueDeclare(queueName1, true, false, false, null);
                channel.QueueBind(queueName1, exchangeName, routingKey1, null);
                channel.QueueDeclare(queueName2, true, false, false, null);
                channel.QueueBind(queueName2, exchangeName, routingKey2, null);
                IBasicProperties propAdding = channel.CreateBasicProperties();
                propAdding.Type = "Adding";
                IBasicProperties propDeleted = channel.CreateBasicProperties();
                propDeleted.Type = "Deleted";

                //выполнение контекстных команд
                while (true)
                {
                    Console.WriteLine(AddingBooks + "\n" + AddingClient + "\n" + DeletedClient);
                    string choice = Console.ReadLine();
                    //включение-отключение добавления книг
                    if (choice == "1")
                    {
                        //включение добавления книг
                        if (AddingBooks == "1 - Запуск добавления книг")
                        {
                            cancelTokenSource = new CancellationTokenSource();
                            token = cancelTokenSource.Token;
                            Console.WriteLine("Ввведите интервал (в секундх) случайного добавления книг");
                            int minInterval;                                                                        //минимальное значение интервала добавления
                            while (true)
                            {
                                Console.WriteLine("Ввведите начальное значение интервала");
                                if (Int32.TryParse(Console.ReadLine(), out minInterval) && minInterval >= 0)
                                    break;
                                else
                                    Console.WriteLine("Не верно введено значение");
                            }
                            int maxInterval;                                                                        //максимальное значение интервала добавления
                            while (true)
                            {
                                Console.WriteLine("Ввведите конечное значение интервала");
                                if (Int32.TryParse(Console.ReadLine(), out maxInterval) && maxInterval > minInterval)
                                    break;
                                else
                                    Console.WriteLine("Не верно введено значение или конечное значение интервала меньше начального значения интервала");
                            }
                            Task clientTask = new Task(() => RandomAddingBooks(minInterval, maxInterval, token, channel, exchangeName, routingKey1));
                            clientTask.Start();                                                                     //запуск метода RandomAddingBooks в другом потоке
                            Console.WriteLine("Успешно");
                            AddingBooks = "1 - Отмена добавления книг";
                        }
                        //отключение запуска книг
                        else if (AddingBooks == "1 - Отмена добавления книг")
                        {
                            cancelTokenSource.Cancel();
                            AddingBooks = "1 - Запуск добавления книг";
                            Console.WriteLine("Успешно");
                        }
                    }
                    //добавление нового клиента
                    else if (choice == "2")
                    {
                        string pattern = @"^[A-Я][а-я]*$";
                        string surname;                                                  //Фамилия              
                        while (true)
                        {
                            Console.WriteLine("Ввведите фамилию клиента");
                            surname = Console.ReadLine();
                            if (Regex.IsMatch(surname, pattern) && surname.Length < 30)
                                break;
                            else
                                Console.WriteLine("Не верно введено значение. Фамилия должна содержать не более 30 прописных букв кириллицы с заглавной буквой");
                        }
                        string name;                                                    //Имя
                        while (true)
                        {
                            Console.WriteLine("Ввведите имя клиента");
                            name = Console.ReadLine();
                            if (Regex.IsMatch(name, pattern) && name.Length < 30)
                                break;
                            else
                                Console.WriteLine("Не верно введено значение. Имя должно содержать не более 30 прописных букв кириллицы с заглавной буквой");
                        }
                        string address;                                                 //электронный адресс
                        while (true)
                        {
                            Console.WriteLine("Ввведите адресс электронной почты клиента");
                            address = Console.ReadLine();
                            pattern = @"^(?("")(""[^""]+?""@)|(([0-9a-z]((\.(?!\.))|[-!#\$%&'\*\+/=\?\^`\{\}\|~\w])*)(?<=[0-9a-z])@))" +
                                @"(?(\[)(\[(\d{1,3}\.){3}\d{1,3}\])|(([0-9a-z][-\w]*[0-9a-z]*\.)+[a-z0-9]{2,17}))$";
                            if (Regex.IsMatch(address, pattern, RegexOptions.IgnoreCase) && address.Length < 30)
                                break;
                            else
                                Console.WriteLine("Не верно введено значение или число символов больше 30");
                        }
                        int pagesPerDay;                                                //Количество читаемых страниц в день                                                                
                        while (true)
                        {
                            Console.WriteLine("Ввведите количество страниц, читаемое клиентом за день");
                            if (Int32.TryParse(Console.ReadLine(), out pagesPerDay) && pagesPerDay <= 1000)
                                break;
                            else
                                Console.WriteLine("Не верно введено значение или количество страниц превышает 1000");
                        }
                        int readingIntervalActive;                                      //Количество дней чтения подряд
                        while (true)
                        {
                            Console.WriteLine("Введите количество дней чтения книг подряд");
                            if (Int32.TryParse(Console.ReadLine(), out readingIntervalActive) && readingIntervalActive >= 0)
                                break;
                            else
                                Console.WriteLine("Не верно введено значение или количество дней меньше 1");
                        }
                        int readingIntervalPassive;                                     //Количество дней отдыха                                                                   
                        while (true)
                        {
                            Console.WriteLine("Введите количество дней между чтением книг");
                            if (Int32.TryParse(Console.ReadLine(), out readingIntervalPassive) && readingIntervalPassive <= 20)
                                break;
                            else
                                Console.WriteLine("Не верно введено значение или количество дней превышает 20");
                        }
                        int countLanguages;                                             //Количество языков
                        while (true)
                        {
                            Console.WriteLine("Введите количество языков, которыми владеет клиент");
                            if (Int32.TryParse(Console.ReadLine(), out countLanguages) && countLanguages <= 5 && countLanguages >= 1)
                                break;
                            else
                                Console.WriteLine("Не верно введено значение или количество языков превышет 5 или меньше 1");
                        }
                        //создание массива языков и уровня владения клиента
                        LevelLanguageSDB[] levelLanguages = new LevelLanguageSDB[countLanguages];
                        List<int> numberLan = new List<int>();
                        for (int i = 0; i < countLanguages; i++)
                        {
                            int numberLanguages;                                            //язык
                            while (true)
                            {
                                Console.WriteLine("Введите язык (1-Русский, 2-Английский, 3-Немецкий, 4-Итальянский, 5-Испанский)");
                                if (Int32.TryParse(Console.ReadLine(), out numberLanguages) && numberLanguages <= 5 && numberLanguages >= 1)
                                {
                                    if (numberLan.Count == 0)
                                    {
                                        numberLan.Add(numberLanguages);
                                        break;
                                    }
                                    else
                                    {
                                        int j = 0;
                                        foreach (int nl in numberLan)
                                        {
                                            if (numberLanguages == nl)
                                            {
                                                Console.WriteLine("Данный язык уже выбран");
                                                break;
                                            }
                                            j++;
                                        }
                                        if (numberLan.Count == 0 || j == numberLan.Count)
                                        {
                                            numberLan.Add(numberLanguages);
                                            break;
                                        }
                                    }
                                }
                                else
                                    Console.WriteLine("Не верно введено значение");
                            }
                            int level;                                              //Уровень владения
                            while (true)
                            {
                                Console.WriteLine("Введите уровень владения языком (от 1 до 10)");
                                if (Int32.TryParse(Console.ReadLine(), out level) && level <= 10 && level >= 1)
                                    break;
                                else
                                    Console.WriteLine("Не верно введено значение");
                            }
                            levelLanguages[i] = new LevelLanguageSDB((BookSDB.languageEnum)numberLanguages, level);
                        }
                        //создание клиента, сериализация в файл JSON и отправка в очередь
                        ClientSDB Client = new ClientSDB(surname, name, address, pagesPerDay, readingIntervalActive, readingIntervalPassive, levelLanguages);
                        string ClientJSON = JsonSerializer.Serialize<ClientSDB>(Client);
                        byte[] messageBodyBytes = Encoding.UTF8.GetBytes(ClientJSON);
                        channel.BasicPublish(exchangeName, routingKey2, propAdding, messageBodyBytes);
                        Console.WriteLine("Успешно");
                    }
                    else if (choice == "3")
                    {
                        string address;                                 //электронный адресс
                        while (true)
                        {
                            Console.WriteLine("Ввведите адресс электронной почты клиента");
                            address = Console.ReadLine();
                            string pattern = @"^(?("")(""[^""]+?""@)|(([0-9a-z]((\.(?!\.))|[-!#\$%&'\*\+/=\?\^`\{\}\|~\w])*)(?<=[0-9a-z])@))" +
                                @"(?(\[)(\[(\d{1,3}\.){3}\d{1,3}\])|(([0-9a-z][-\w]*[0-9a-z]*\.)+[a-z0-9]{2,17}))$";
                            if (Regex.IsMatch(address, pattern, RegexOptions.IgnoreCase) && address.Length < 30)
                                break;
                            else
                                Console.WriteLine("Не верно введено значение или число символов больше 30");
                        }
                        //сериализация адресса в JSON и отправка в очередь
                        byte[] messageBodyBytes = Encoding.UTF8.GetBytes(address);
                        channel.BasicPublish(exchangeName, routingKey2, propDeleted, messageBodyBytes);
                        Console.WriteLine("Успешно");
                    }
                    else
                    {
                        Console.WriteLine("Данного кода операции не существует");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                if (conn != null) conn.Close();
                if (channel != null) channel.Close();
            }
        }

        /// <summary>
        /// Метод случайного добавления книг
        /// </summary>
        /// <param name="minInterval">Минимальное значение интервала добавления</param>
        /// <param name="maxInterval">Максимальное значение интервала добавления</param>
        /// <param name="token">Отменяющий токен</param>
        /// <param name="channel">Канал связи с RabbitMQ</param>
        /// <param name="exchangeName">Имя точки роутинга</param>
        /// <param name="routingKey1">Ключ привязки точки роутинга к очереди</param>
        private static void RandomAddingBooks(int minInterval, int maxInterval, CancellationToken token, IModel channel, string exchangeName, string routingKey1)
        {
            Random random = new Random();
            string name;
            int pages;
            BookSDB.languageEnum language;
            BookSDB Book;
            while (!token.IsCancellationRequested)
            {
                name = "Book" + random.Next(1, 100000).ToString();
                pages = random.Next(5, 1500);
                language = (BookSDB.languageEnum)random.Next(1, 6);
                //содание объекта книги
                Book = new BookSDB(language, pages, name);
                int TimeSleep = random.Next(minInterval, maxInterval) * 1000;
                Thread.Sleep(TimeSleep);
                //сериализация объекта книги в JSON и отправка в очередь
                string BookJSON = JsonSerializer.Serialize<BookSDB>(Book);
                byte[] messageBodyBytes = Encoding.UTF8.GetBytes(BookJSON);
                channel.BasicPublish(exchangeName, routingKey1, null, messageBodyBytes);
            }
        }
    }
}
