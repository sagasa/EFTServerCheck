using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace EFTServerCheck
{
    internal class ApiManager
    {
        private static readonly Dictionary<string, string> ip2location=new();

        public static Task<string> GetLocation(string ip)
        {
            return Task.Factory.StartNew(() => {
                using (var client = new HttpClient())
                {
                    var url = @$"http://ip-api.com/json/{ip}?fields=status,countryCode,city";
                    var response = client.GetAsync(url).Result;

                    if(response.StatusCode != System.Net.HttpStatusCode.OK)
                        throw new Exception($"api call failed code={response.StatusCode} url={url}");   
                    
                    var location = JsonSerializer.Deserialize<LocationResponce>(response.Content.ReadAsStringAsync().Result)!;

                    if (location.status != "success")
                        throw new Exception($"api call failed url={url}");

                    return location.Format();
                }
            });
        }

        public static Task<Dictionary<string,string>> GetLocation(IList<string> ips)
        {
            return Task.Factory.StartNew(() => {
                using (var client = new HttpClient())
                {
                    Dictionary<string, string> result = new();

                    List<string> reqList = new();
                    List<Task<HttpResponseMessage>> taskList = new();
                    void dispatch()
                    {
                        var json = @$"[{string.Join(",", reqList)}]";
                        reqList.Clear();
                        var content = new StringContent(json, Encoding.UTF8);
                        taskList.Add(client.PostAsync("http://ip-api.com/batch", content)); // POST
                    }

                    //100件で分割
                    int count = 0;
                    foreach (var ip in ips)
                    {
                        reqList.Add(@"{""query"": """ + ip + @""", ""fields"": ""status,city,countryCode,query""}");
                        result[ip] = "N/A";
                        if (100 <= ++count)
                        {
                            dispatch();
                            count = 0;
                        }
                    }
                    dispatch();

                    

                    foreach (var task in taskList)
                    {
                        var res = task.Result;

                        if (res.StatusCode != System.Net.HttpStatusCode.OK)
                        {
                            Console.WriteLine("api error");
                            Console.WriteLine(res);
                        }
                        else
                        {
                            IList<LocationResponce> responces = JsonSerializer.Deserialize<IList<LocationResponce>>(res.Content.ReadAsStringAsync().Result)!;

                            foreach (var responce in responces)
                            {
                                if (responce.status == "success")
                                {

                                    result[responce.query] = $"{responce.countryCode} {responce.city}";
                                }
                            }
                        }
                    }
                    return result;
                }
                
            });
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
