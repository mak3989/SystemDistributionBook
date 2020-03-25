using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;
using MySql.Data.MySqlClient;

namespace WcfServiceSDB
{
    [ServiceContract]
    public interface IServiceSDB
    {

        [OperationContract]
        DataSet GetClientInfo(string address);
        [OperationContract]
        DataSet GetBook(int statusReading, string language, string name, string address);
        [OperationContract]
        DataSet GetClientStatistics(string statusSubscription, string language, string address);
    }
}
