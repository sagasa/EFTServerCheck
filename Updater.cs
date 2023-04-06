using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static EFTServerCheck.ViewManager;

namespace EFTServerCheck
{
    internal static class Updater
    {
        private static readonly HttpClient _client = new();
        private static readonly Regex _tag = new Regex(@"""tag_name"":""(?:.*?)(\d+).(\d+)(?:.(\d+)|)""");

        const string FILE_DOWNLOAD = @"EFTServerChecker.exe.download";
        const string FILE_OLD = @"EFTServerChecker.exe.old";
        const string FILE_CURRENT = @"EFTServerChecker.exe";

        static Updater()
        {
            _client.DefaultRequestHeaders.Add("User-Agent", ".NET Foundation Repository Reporter");
        }

        public static void CheckUpdate()
        {
            File.Delete(FILE_OLD);

            var line = PrintLine();
            line.Replace("checking update", FOREGROUND_YELLOW);

            var res = _client.GetAsync("https://api.github.com/repos/sagasa/EFTServerChecker/releases/latest").Result;

            if (!res.IsSuccessStatusCode)
            {
                line.Replace("check feild", FOREGROUND_DARKRED);
                return;
            }
            var json = res.Content.ReadAsStringAsync().Result;


            Match match = _tag.Match(json);

            if (!match.Success)
            {
                line.Replace("check feild", FOREGROUND_DARKRED);
                return;
            }
            var latest = new Version(
                int.Parse(match.Groups[1].Value),
                int.Parse(match.Groups[2].Value),
                match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0
                );

            var assembly = Assembly.GetExecutingAssembly();
            var current = assembly.GetName().Version!;

            if (latest<=current)
            {
                line.Delete();
                return;
            }

            line.Replace("program outdated press y to download", FOREGROUND_DARKRED);
            if (!WaitY())
            {
                line.Delete();
                return;
            }

            ProgressInfo progressinfo = new ProgressInfo();
            var task = DownloadImgAsync("https://github.com/sagasa/EFTServerChecker/releases/latest/download/EFTServerChecker.exe", FILE_DOWNLOAD, progressinfo);

            do
            {
                Thread.Sleep(100);
                float progress = progressinfo.Progress;
                int length = 30;
                int i = (int)(length * progress);

                var pstr = (progress * 100).ToString("F1") + "%";

                StringBuilder sb = new StringBuilder('[');
                sb.Append('[');
                sb.Append('=', i);
                sb.Append(' ', length - i);
                sb.Append(']');
                sb.Remove(length / 2 - 2, pstr.Length);
                sb.Insert(length / 2 - 2, pstr);
                line.Replace("downloading" + sb, FOREGROUND_YELLOW);
            } while (!task.IsCompleted);

            File.Move(FILE_CURRENT, FILE_OLD,true);
            File.Move(FILE_DOWNLOAD, FILE_CURRENT);

            line.Replace("press y to restart", FOREGROUND_CYAN);

            if (!WaitY())
            {
                line.Delete();
                return;
            }

            Process.Start(FILE_CURRENT);
            Environment.Exit(0);
        }

        static bool WaitY()
        {
            while (true)
            {
                ConsoleKeyInfo i;
                while (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.Y)
                    {
                        return true;
                    }else if (key == ConsoleKey.N)
                    {
                        return false;
                    }
                }
                Thread.Sleep(900);
            }
        }

        static async Task DownloadImgAsync(string imgUrl, string downloadPath, ProgressInfo progress)
        {
            
            //画像をダウンロード
            var response = await _client.GetAsync(imgUrl,HttpCompletionOption.ResponseHeadersRead);

            
            //ステータスコードで成功したかチェック
            if (!response.IsSuccessStatusCode) return;

            //ファイルサイズ
            progress.BytesTotal = long.Parse(response.Content.Headers.GetValues("Content-Length").First());
            
            //保存
            using var stream = await response.Content.ReadAsStreamAsync();
            using var outStream = File.Create(downloadPath);            
            stream.CopyToWithProgress(outStream, progress);
            
        }

        delegate void ProgressFunc(long count);

        static void CopyToWithProgress(this Stream from, Stream dest, ProgressInfo progress, int bufferSize = 81920)
        {
            var buffer = new byte[bufferSize];
            int count;
            while ((count = from.Read(buffer, 0, buffer.Length)) != 0)
            {
                dest.Write(buffer, 0, count);
                progress.BytesTransfered += count;
            }
        }


        public class ProgressInfo
        {
            public long BytesTotal { get; set; }
            public long BytesTransfered { get; set; }
            public float Progress { get => BytesTransfered==0?0:(float)(BytesTransfered / (double)BytesTotal); }
        }

    }
}
