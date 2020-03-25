using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;
using System.Data;
using MySql.Data.MySqlClient;

namespace WcfServiceSDB
{
    public class ServiceSDB : IServiceSDB
    {
        /// <summary>
        /// Соединение с MySQL
        /// </summary>
        MySqlConnection connection;
        /// <summary>
        /// Строка соединения с MySQL
        /// </summary>
        string connectionString;
        /// <summary>
        /// Список используемых языков
        /// </summary>
        List<string> listLanguage;
        /// <summary>
        /// Конструтор объекта
        /// </summary>
        public ServiceSDB()
        {
            connectionString = "Server=127.0.0.1;Database=systemdistributionbook;port=3306;User Id=root;password=n1477353";
            listLanguage = new List<string> { "Русский", "Английский", "Немецкий", "Итальянский", "Испанский" };
        }
        /// <summary>
        /// Возвращает информацию о клиенте в 2 таблицах
        /// </summary>
        /// <param name="address">Электронный адресс клиента</param>
        /// <returns></returns>
        public DataSet GetClientInfo(string address)
        {
            string sql;
            DataSet ds = new DataSet();
            ds.Tables.Add();
            ds.Tables.Add();
            for (int i = 0; i <= 1; i++)
            {
                //запрос
                if (i == 0) sql = "SELECT * FROM clients WHERE Address = @Address";
                else sql = "SELECT * FROM LevelLanguages WHERE AddressClient = @Address";
                using (connection = new MySqlConnection(connectionString))
                {
                    connection.Open();
                    MySqlCommand command = new MySqlCommand(sql, connection);
                    MySqlParameter AddressParam = new MySqlParameter("@Address", address);
                    command.Parameters.Add(AddressParam);
                    MySqlDataAdapter adapter = new MySqlDataAdapter(command);
                    adapter.Fill(ds.Tables[i]);
                }
            }
            return ds;
        }
        /// <summary>
        /// Возвращает информацию о книгах
        /// </summary>
        /// <param name="statusReading">статус чтения (1-прочитана,2-читается)</param>
        /// <param name="language">Язык книги</param>
        /// <param name="name">Название книги</param>
        /// <param name="address">Электронный адресс клиента</param>
        /// <returns></returns>
        public DataSet GetBook(int statusReading, string language, string name, string address)
        {
            DataSet ds = new DataSet();
            string sqlWhere = "";
            string sqlWherePr;
            MySqlCommand command = new MySqlCommand();
            //проверка статуса, при совпадении с 1 или 2 - добавляется в фильтр, при несовпадении в фильтр не добавляется
            switch (statusReading)
            {
                case 1:
                    sqlWhere = " DataReading IS NOT NULL";
                    break;
                case 2:
                    sqlWhere = " DataReading IS NULL";
                    break;
                default:
                    break;                   
            }
            //проверка языка, при совпадении - добавляется в фильтр, при несовпадении в фильтр не добавляется
            foreach (string lang in listLanguage)
            {
                if (language == lang)
                {
                    sqlWherePr = " Language = @language";
                    if (sqlWhere != "")
                        sqlWhere = sqlWhere + " AND" + sqlWherePr;
                    else
                        sqlWhere = sqlWhere + sqlWherePr;
                    MySqlParameter languageParam = new MySqlParameter("@language", language);
                    command.Parameters.Add(languageParam);
                }
            }
            //проверка названия книги, при пустом значении или NULL в фильтр не добавляется
            if (name != "" && name != null)
            {
                sqlWherePr = " Name = @name";
                if (sqlWhere != "")
                    sqlWhere = sqlWhere + " AND" + sqlWherePr;
                else
                    sqlWhere = sqlWhere + sqlWherePr;
                MySqlParameter nameParam = new MySqlParameter("@name", name);
                command.Parameters.Add(nameParam);
            }
            // проверка адресса клиента, при пустом значении или NULL в фильтр не добавляется
            if (address != "" && address != null)
            {
                sqlWherePr = " AddressClient = @address";
                if (sqlWhere != "")
                    sqlWhere = sqlWhere + " AND" + sqlWherePr;
                else
                    sqlWhere = sqlWhere + sqlWherePr;
                MySqlParameter addressParam = new MySqlParameter("@address", address);
                command.Parameters.Add(addressParam);
            }

            if (sqlWhere != "")
                sqlWhere = " WHERE" + sqlWhere;
            //запрос
            command.CommandText = "SELECT * FROM books" + sqlWhere;
            using (connection = new MySqlConnection(connectionString))
            {
                connection.Open();
                command.Connection = connection;
                MySqlDataAdapter adapter = new MySqlDataAdapter(command);
                adapter.Fill(ds);
            }
            return ds;
        }
        /// <summary>
        /// Возвращает статистику клиента
        /// </summary>
        /// <param name="statusSubscription">Статус подписки</param>
        /// <param name="language">Язык</param>
        /// <param name="address">Электронный адресс клиента</param>
        /// <returns></returns>
        public DataSet GetClientStatistics(string statusSubscription, string language, string address)
        {
            DataSet ds = new DataSet();
            string sqlWhereClient = "";
            string sqlWhereBook = "";
            string sqlWherePr;
            MySqlCommand command = new MySqlCommand();
            //проверка статуса, при совпадении с "Подписан" или "Отписан" - добавляется в фильтр, при несовпадении в фильтр не добавляется
            if (statusSubscription == "Подписан" || statusSubscription == "Отписан")
            {
                sqlWhereClient = " c.Subscription = @subscription";
                MySqlParameter subscriptionParam = new MySqlParameter("@subscription", statusSubscription);
                command.Parameters.Add(subscriptionParam);
            }
            //проверка языка, при совпадении - добавляется в фильтр, при несовпадении в фильтр не добавляется
            foreach (string lang in listLanguage)
            {
                if (language == lang)
                {
                    sqlWherePr = " ll.Language = @language";
                    if (sqlWhereClient != "")
                        sqlWhereClient = sqlWhereClient + " AND" + sqlWherePr;
                    else
                        sqlWhereClient = sqlWhereClient + sqlWherePr;
                    sqlWhereBook = " WHERE Language = @language";
                    MySqlParameter languageParam = new MySqlParameter("@language", language);
                    command.Parameters.Add(languageParam);
                }
            }
            // проверка адресса клиента, при пустом значении или NULL в фильтр не добавляется
            if (address != "" && address != null)
            {
                sqlWherePr = " c.Address = @address";
                if (sqlWhereClient != "")
                    sqlWhereClient = sqlWhereClient + " AND" + sqlWherePr;
                else
                    sqlWhereClient = sqlWhereClient + sqlWherePr;
                MySqlParameter addressParam = new MySqlParameter("@address", address);
                command.Parameters.Add(addressParam);
            }

            if (sqlWhereClient != "")
                sqlWhereClient = " WHERE" + sqlWhereClient;
            //запрос
            command.CommandText = "SELECT Address, COUNT(b.Name) AS CountBook, SUM(Pages) AS CountPages FROM " +
                    "(SELECT DISTINCT Address FROM clients AS c " +
                    "JOIN levellanguages AS ll " +
                    "ON c.Address = ll.AddressClient " +
                    sqlWhereClient + ") AS Addr " +
                    "LEFT JOIN (SELECT * FROM books " +
                    sqlWhereBook + ") AS b " +
                    "ON Addr.Address = b.AddressClient " +
                    "GROUP BY Address";
            using (connection = new MySqlConnection(connectionString))
            {
                connection.Open();
                command.Connection = connection;
                MySqlDataAdapter adapter = new MySqlDataAdapter(command);
                adapter.Fill(ds);
            }
            return ds;
        }
    }
}
