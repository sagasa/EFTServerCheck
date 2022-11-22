using System.Text.Json;
using System.IO;
using System.Text.RegularExpressions;
using System.Text;

Regex RegSessionInfo = new Regex(@"^(.*?)\|(?:.*)Ip: (.*?),(?:.*)Location: (.*?),(?:.*)shortId: (.*?)'");
Regex RegTime = new Regex(@"^(.*?)\|");
const string GameStart = @"TRACE-NetworkGameCreate 6";
const string ServerConnect = @"TRACE-NetworkGameCreate profileStatus:";
const string ConfigPath = "./config.json";

//コンフィグ読み込み
Config config = new Config();
if (!File.Exists(ConfigPath))
{
    File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config));
}


try
{
    config = JsonSerializer.Deserialize<Config>(WriteSafeReadAllString(ConfigPath))!;
}
catch (Exception e)
{
    File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config));
    Console.WriteLine("config file is broken");
    Console.ReadLine();
}



string Dir = config.Path;//"G:\\Battlestate Games\\EFT\\Logs";

try
{
    
    var dirs = from dir in Directory.GetDirectories(Dir)
               let time = Directory.GetCreationTime(dir).ToBinary()
               orderby time descending
               select dir;




    var sessions = new List<SessionData>();
    int limit = config.Limit;

    void ReadLogs(string dir)
    {
        foreach (var file in Directory.GetFiles(dir))
        {
            if (file.EndsWith("application.log"))
            {
                var lines = WriteSafeReadAllLines(file);

                DateTime? lastStartTime = null;
                foreach (var line in lines.Reverse())
                {
                    //マッチ開始のログ
                    if (line.Contains(GameStart))
                    {
                        Match match = RegTime.Match(line);
                        if (match.Success)
                        {

                            lastStartTime = DateTime.Parse(match.Groups[1].Value);
                        }
                        else
                        {
                            Console.WriteLine($"error cant parse string {line}");
                        }
                    }
                    //サーバー接続のログ
                    if (line.Contains(ServerConnect))
                    {
                        Match match = RegSessionInfo.Match(line);
                        if (match.Success)
                        {



                            //開始時間を消費
                            var time = DateTime.Parse(match.Groups[1].Value);
                            var ip = match.Groups[2].Value;
                            var map = match.Groups[3].Value;
                            var id = match.Groups[4].Value;

                            //Console.WriteLine($"time:{time} ip:{ip} map:{map} id:{id}");

                            var data = new SessionData(ip, map, id, time);

                            //開始時間がないならロスコネ判定
                            if (lastStartTime == null)
                            {
                                data.LostConn = true;
                            }
                            else
                            {
                                data.Time = (DateTime)lastStartTime;
                                lastStartTime = null;
                            }

                            //1つ前と比較してロスコネ判定
                            if (sessions.Count > 0)
                            {
                                var last = sessions.Last();
                                if (data.Id == last.Id)
                                {
                                    data.LostConn = true;
                                    sessions.RemoveAt(sessions.Count - 1);
                                    limit++;
                                }
                            }
                            sessions.Add(data);

                            limit--;
                            if (limit == 0)
                            {
                                return;
                            }

                        }
                        else
                        {
                            Console.WriteLine($"error cant parse string {line}");
                        }

                    }

                }
                return;
            }
        }
    }

    //ディレクトリ走査
    foreach (var dir in dirs)
    {

        ReadLogs(dir);
        if (limit == 0) break;
    }

    //IPテーブルを作成
    Dictionary<string, string> ipMap = new Dictionary<string, string>();

    List<SessionData> viewSessions = new List<SessionData>();


    //IPテーブルに代入
    foreach (var session in sessions)
    {
        viewSessions.Add(session);
        ipMap[session.Ip] = "Nan";
    }

    //APIから位置を取得
    using (var client = new HttpClient())
    {

        List<string> reqList = new List<string>();
        List<Task<HttpResponseMessage>> taskList = new List<Task<HttpResponseMessage>>();
        void dispatch()
        {
            var json = @$"[{string.Join(",", reqList)}]";
            reqList.Clear();
            var content = new StringContent(json, Encoding.UTF8);
            taskList.Add(client.PostAsync("http://ip-api.com/batch", content)); // POST
        }

        
        int count = 0;
        foreach (var entry in ipMap)
        {
            reqList.Add(@"{""query"": """ + entry.Key + @""", ""fields"": ""status,city,countryCode,query""}");
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
                        ipMap[responce.query] = $"{responce.countryCode} {responce.city}";
                    }
                }
            }
        }
        //Console.WriteLine($"IP: {IP}");
    }

    //情報の集計
    int lostConnCount = 0;
    var server2LostConn = new Dictionary<string, int>();

    var map2Count = new Dictionary<string, int>();

    foreach (var session in viewSessions)
    {
        if (session.LostConn)
        {
            var server = ipMap[session.Ip];
            if (!server2LostConn.ContainsKey(server))
            {
                server2LostConn[server] = 0;
            }
            server2LostConn[server]++;
            lostConnCount++;
        }
        if(!map2Count.ContainsKey(session.Map))
            map2Count[session.Map] = 0;
        map2Count[session.Map]++;
    }

    //出力
    viewSessions.Reverse();
    foreach (var session in viewSessions)
    {
        if (session.LostConn)
        {
            Console.ForegroundColor = ConsoleColor.DarkRed;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
        }
        Console.WriteLine($"Time: {session.Time.ToString("yyyy/MM/dd HH:mm:ss")}   Map: {ConvertMapName(session.Map).PadRight(14)} Server: {ipMap[session.Ip]}");
    }
    Console.ResetColor();

    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine();


    PrintCenter($"total count {sessions.Count}", ' ', ConsoleColor.Blue);
    {

        var text = $" lost connection count {lostConnCount} ";
        PrintCenter(text,'-', ConsoleColor.Cyan, ConsoleColor.Red);
    }
    

    {
        var count = 0;
        var sorted = from entry in server2LostConn orderby entry.Value descending select entry;
        foreach (var entry in sorted)
        {
            var text = $"{entry.Key}: {entry.Value}".PadRight(24);
            count += text.Length;
            if (Console.WindowWidth < count+10)
            {
                Console.WriteLine();
                count = text.Length;
            }
            Console.Write(text);
            
        }
        Console.WriteLine();
    }
}
catch (DirectoryNotFoundException e)
{
    Console.WriteLine($"cant find log dir at '{Dir}'. check config file");

}
catch (Exception e)
{

    Console.WriteLine(e);
}
finally
{
    Console.ReadLine();
}


static void PrintCenter(String text,char fill,ConsoleColor textColor = ConsoleColor.White, ConsoleColor fillColor = ConsoleColor.White)
{
    Console.ForegroundColor = fillColor;
    for (int i = 0; i < (Console.WindowWidth - text.Length) / 2 - 4; i++)
        Console.Write(fill);
    Console.ForegroundColor = textColor;
    Console.Write(text);
    Console.ForegroundColor = fillColor;
    for (int i = 0; i < (Console.WindowWidth - text.Length) / 2 - 4; i++)
        Console.Write(fill);
    Console.ResetColor();
    Console.WriteLine();
}

static string WriteSafeReadAllString(String path)
{
    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
    using (var sr = new StreamReader(stream))
    {
        return sr.ReadToEnd();
    }
}


static string[] WriteSafeReadAllLines(String path)
{
    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
    using (var sr = new StreamReader(stream))
    {
        List<string> file = new List<string>();
        while (!sr.EndOfStream)
        {
            file.Add(sr.ReadLine());
        }

        return file.ToArray();
    }
}

static string ConvertMapName(string name)
{
    switch (name)
    {
        case "factory4_day":
            return "FactoryDay";
        case "factory4_night":
            return "FactoryNight";
        case "RezervBase":
            return "Reserve";
        case "bigmap":
            return "Customs";
        case "laboratory":
            return "Laboratory"; 
        default:
            return name;
    }
    
}


class Config
{
    public string Path { get; set; } = @"C:\Battlestate Games\EFT\Logs";
    public int Limit { get; set; } = 20; 
}
public class SessionData {
    public SessionData(string ip,string map,string id,DateTime time)
    {
        Ip = ip;
        Map = map;
        Id = id;
        Time = time;
    }
    public override string ToString() {
        return $"Time:{Time} Ip:{Ip} Map:{Map} Id:{Id} LostConn:{LostConn}";
    }
    public string Ip;
    public string Map;
    public string Id;
    public bool LostConn = false;
    public DateTime Time;
}

public class LocationResponce
{
    public string countryCode { get; set; }
    public string city { get; set; }
    public string status { get; set; }
    public string query { get; set; }
    
}