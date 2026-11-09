using System;
using System.Linq;
using System.Net;
using DnsClient;

namespace core
{
    internal static class DnsLookup
    {
        private static readonly LookupClient client = new LookupClient(
            new LookupClientOptions(IPAddress.Parse("8.8.8.8"), IPAddress.Parse("1.1.1.1"))
            {
                Timeout = TimeSpan.FromSeconds(5),
                Retries = 1,
                UseCache = true
            });

        /// <summary>Devuelve el primer texto TXT del nombre, o null si no hay.</summary>
        public static string FirstTxt(string name)
        {
            var response = client.Query(name, QueryType.TXT);
            var record = response.Answers.TxtRecords().FirstOrDefault();
            return record == null ? null : record.Text.FirstOrDefault();
        }
    }
}