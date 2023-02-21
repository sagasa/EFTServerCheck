using System.Text.Json;
using System.IO;
using System.Text.RegularExpressions;
using System.Text;
using EFTServerCheck;
using System.Diagnostics;
using System.Windows.Forms;
using static EFTServerCheck.ViewManager;

namespace EFTServerCheck
{
    public class ServerChecker
    {
        [STAThread]
        static void Main()
        {
            
            var checker = new ServerChecker();
            checker.LoadConfig();
            checker.RunLoop();
        }

        const string ConfigPath = "./config.json";


        void RunLoop()
        {
            try
            {
                while (true)
                {
                    SessionManager.UpdateLogs();
                    Thread.Sleep(900);
                }
            }
            catch (Exception e) when (e is FileNotFoundException || e is DirectoryNotFoundException)
            {
                PrintLine($"cant find log dir at '{SessionManager.Root}'. check config file");
                ConfigDialog();
            }
            catch (Exception e)
            {

                Console.Error.WriteLine(e);
            }
            finally
            {

                Console.ReadLine();
            }
        }

        void ConfigDialog()
        {
            var dialog = new FolderBrowserDialog();
            dialog.InitialDirectory = "c:";
            var state = dialog.ShowDialog();
            if (state == DialogResult.OK)
            {
                var path = dialog.SelectedPath;
                if (Path.GetFileName(path)== "EFT")
                {
                    path = Path.Combine(path,"Logs\\");
                }
                else if(Path.GetFileName(path)== "Battlestate Games")
                {
                    path = Path.Combine(path, "EFT\\Logs\\");
                }

                Config config = new();

                SessionManager.Root = path;
                config.Path = SessionManager.Root;
                config.Limit = SessionManager.Limit;

                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config));
                RunLoop();
            }
        }

        void LoadConfig()
        {
            Config config = new();
            //コンフィグ読み込み
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
                PrintLine("config file is broken");
                Console.ReadLine();
            }
            SessionManager.Root = config.Path;
            SessionManager.Limit = config.Limit;
        }


        static string WriteSafeReadAllString(String path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(stream))
            {
                return sr.ReadToEnd();
            }
        }
    }

    class Config
    {
        public string Path { get; set; } = @"C:\Battlestate Games\EFT\Logs";
        public int Limit { get; set; } = 20;
    }
}
