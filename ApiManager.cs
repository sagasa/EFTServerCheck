using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static EFTServerCheck.ViewManager;

namespace EFTServerCheck
{
    internal class ApiManager
    {
        private static readonly HttpClient _client = new();
        private static readonly Dictionary<string, Task<string>> _ip2location = new();

        public static Task<string> ReqLocation(string ip)
        {
            if (_ip2location.ContainsKey(ip))
            {
                return _ip2location[ip];
            }
            var task = Task.Factory.StartNew(() =>
            {

                //PrintLine("Make Req");

                var url = @$"http://ip-api.com/json/{ip}?fields=status,countryCode,city";
                var response = _client.GetAsync(url).Result;

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                    throw new Exception($"api call failed code={response.StatusCode} url={url}");

                var location = JsonSerializer.Deserialize<LocationResponce>(response.Content.ReadAsStringAsync().Result)!;

                if (location.status != "success")
                    throw new Exception($"api call failed url={url}");

                return location.Format();

            });
            _ip2location[ip] = task;
            return task;
        }

        public static void ReqLocation(ISet<string> ips)
        {
            ips.ExceptWith(_ip2location.Keys);
            //PrintLine("Make batch req");
            List<string> reqList = new();
            void dispatch()
            {
                var json = @$"[{string.Join(",", reqList.Select(ip => @"{""query"": """ + ip + @""", ""fields"": ""status,city,countryCode,query""}").ToList())}]";
                var content = new StringContent(json, Encoding.UTF8);

                var task = _client.PostAsync("http://ip-api.com/batch", content).ContinueWith(response =>
                {
                    if (response.Result.StatusCode != System.Net.HttpStatusCode.OK)
                        throw new Exception($"api call failed code={response.Result.StatusCode}");
                    var list = JsonSerializer.Deserialize<IList<LocationResponce>>(response.Result.Content.ReadAsStringAsync().Result)!;
                    return list.ToDictionary(res => res.query, res => res.status == "success" ? $"{res.countryCode} {res.city}" : "error");
                });


                //通知
                reqList.ForEach(ip =>
                {
                    if (!_ip2location.ContainsKey(ip))
                    {
                        _ip2location[ip] = task.ContinueWith(res => res.Result[ip]);
                    }
                });
                reqList.Clear();
            }

            //100件で分割
            int count = 0;
            foreach (var ip in ips)
            {
                reqList.Add(ip);

                if (100 <= ++count)
                {
                    dispatch();
                    count = 0;
                }
            }
            dispatch();
        }


        private class LocationResponce
        {
            public string countryCode { get; set; } = "N/A";
            public string city { get; set; } = "N/A";
            public string status { get; set; } = "N/A";
            public string query { get; set; } = "N/A";

            internal string Format()
            {
                return $"{countryCode} {city}";
            }
        }
    }


}
