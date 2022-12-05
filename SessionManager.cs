using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EFTServerCheck
{
    internal class SessionManager
    {
        static Regex RegSessionInfo = new Regex(@"^(.*?)\|(?:.*)Ip: (.*?),(?:.*)Location: (.*?),(?:.*)shortId: (.*?)'");
        static Regex RegTime = new Regex(@"^(.*?)\|");
        const string GameStart = @"TRACE-NetworkGameCreate 6";
        const string ServerConnect = @"TRACE-NetworkGameCreate profileStatus:";



        internal static string Root = "";
        private static List<SessionData> sessions = new();
        private static int limit = -1;

        private static Dictionary<string,long> _file2size = new();
        private static HashSet<string> _dirs = new();
        private static string? _latestLog;

        public static void UpdateLogs()
        {
            if(_latestLog != null)
                CheckLog(_latestLog);
            CheckDir();
        }

        private static void CheckDir()
        {
            var dirs = from dir in Directory.GetDirectories(Root)
                       let time = Directory.GetCreationTime(dir).ToBinary()
                       orderby time descending
                       select dir;

            //ディレクトリ走査
            foreach (var dir in dirs)
            {
                if (!_dirs.Contains(dir))
                {
                    _dirs.Add(dir);
                    var file = Directory.GetFiles(dir).FirstOrDefault(file => file.EndsWith("application.log"));
                    if (file == null) continue;
                    CheckLog(file);
                }
            }
        }

        private static void CheckLog(string file)
        {
            
            var size = new FileInfo(file).Length;
            //既に見たか確認
            if (_file2size.ContainsKey(file) && _file2size[file] == size)return;
            
            _file2size[file] = size;

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


        public class SessionData
        {
            public SessionData(string ip, string map, string id, DateTime time)
            {
                Ip = ip;
                Map = map;
                Id = id;
                Time = time;
            }
            public override string ToString()
            {
                return $"Time:{Time} Ip:{Ip} Map:{Map} Id:{Id} LostConn:{LostConn}";
            }
            public string Ip;
            public string Map;
            public string Id;
            public bool LostConn = false;
            public DateTime Time;
        }
    }
}
