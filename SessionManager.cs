using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static EFTServerCheck.ViewManager;

namespace EFTServerCheck
{
    internal class SessionManager
    {
        static Regex RegSessionInfo = new Regex(@"^(.*?)\|(?:.*)Ip: (.*?),(?:.*)Location: (.*?),(?:.*)shortId: (.*?)'");
        static Regex RegTime = new Regex(@"^(.*?)\|");
        const string GameStart = @"TRACE-NetworkGameCreate 6";
        const string ServerConnect = @"TRACE-NetworkGameCreate profileStatus:";



        internal static string Root = "";
        internal static int Limit = 6;
        //古いものから順に格納
        private static List<SessionData> _sessions = new();


        private static Dictionary<string, long> _file2size = new();
        private static HashSet<string> _dirs = new();
        private static string? _latestLog;

        public static void UpdateLogs()
        {
            if (_latestLog != null)
            {
                var sessions = ReadLog(_latestLog, prev: _sessions.LastOrDefault());
                sessions.Reverse();
                _sessions.AddRange(sessions);
            }
            CheckDir();

            _sessions.ForEach(session => session.Print());
        }

        private static void CheckDir()
        {
            var dirs = from dir in Directory.GetDirectories(Root)
                       let time = Directory.GetCreationTime(dir).ToBinary()
                       orderby time descending
                       select dir;

            //最新のログを記録
            _latestLog = ToLogPath(dirs.First());

            var sessions = new List<SessionData>();
            //初回なら制限を適応
            var limit = _dirs.Count == 0 ? Limit : int.MaxValue;

            SessionData? prev = _sessions.Count == 0 ? null : _sessions[_sessions.Count - 1];
            SessionData? next = null;

            //ディレクトリ走査
            foreach (var dir in dirs)
            {
                if (!_dirs.Contains(dir))
                {
                    _dirs.Add(dir);
                    var file = ToLogPath(dir);
                    if (!File.Exists(file)) continue;

                    //リミット以内なら
                    if (sessions.Count < limit)
                    {
                        var read = ReadLog(file, prev, next);

                        //存在するなら
                        if (read.Count != 0)
                        {
                            next = read[read.Count - 1];
                        }

                        sessions.AddRange(read);
                    }
                }
            }
            //トリム
            if (limit < sessions.Count)
            {
                sessions.RemoveRange(limit, sessions.Count - limit);
            }

            sessions.Reverse();
            _sessions.AddRange(sessions);

        }

        //新規追加分を読み込む 新しい順
        private static List<SessionData> ReadLog(string file, SessionData? prev = null, SessionData? next = null)
        {
            var prevPos = _file2size.GetValueOrDefault(file, 0);
            var size = new FileInfo(file).Length;
            //既に見たか確認
            if (_file2size.ContainsKey(file) && _file2size[file] == size) return new();

            _file2size[file] = size;

            var sessions = new List<SessionData>();
            var lines = WriteSafeReadAllLines(file, prevPos);

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
                        PrintLine($"error cant parse string {line}");
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

                        //PrintLine($"time:{time} ip:{ip} map:{map} id:{id}");

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
                        //古いものと比較してロスコネ判定
                        if (prev?.Id == data.Id)
                        {
                            prev.LostConn = true;
                            prev.Replace();
                        }
                        //新しいものと比較してロスコネ判定
                        else if (next?.Id == data.Id)
                        {
                            next.LostConn = true;
                            next.Time = data.Time;
                            next.Replace();
                        }
                        else
                        {
                            sessions.Add(data);
                            next = data;
                        }
                    }
                    else
                    {
                        PrintLine($"error cant parse string {line}");
                    }
                }

            }
            return sessions;


        }

        static string[] WriteSafeReadAllLines(String path, long start)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(stream))
            {
                stream.Seek(start, SeekOrigin.Begin);
                List<string> file = new List<string>();
                while (!sr.EndOfStream)
                {
                    file.Add(sr.ReadLine());
                }

                return file.ToArray();
            }
        }


        static string ToLogPath(string dir) =>
            Path.Combine(dir, Path.GetFileName(dir)[4..] + " application.log");


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
            public void Print()
            {
                //サーバー名を取得する
                ApiManager.ReqLocation(Ip).ContinueWith(loc =>
                {
                    Location = loc.Result;
                    Replace();
                });
                if (_line == null)
                    _line = PrintLine();
                Replace();
                
            }
            public void Replace()
            {
                if (_line == null) return;
                var text = $"Time: {Time.ToString("yyyy/MM/dd HH:mm:ss")}   Map: {ConvertMapName(Map).PadRight(14)} Server: {Location.PadRight(30)}";
                var color = LostConn ? ConsoleColor.DarkRed : ConsoleColor.Green;
                _line.Replace(text, color);
            }

            private LineEntry? _line;

            public string Location = "Checking...";
            public string Ip;
            public string Map;
            public string Id;
            public bool LostConn = false;
            public DateTime Time;

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
    }
}
