using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using System.Diagnostics;
using MySql.Data.MySqlClient;
using YAMS;

namespace YAMS
{
    public struct Vector
    {
        public double x;
        public double y;
        public double z;
    }
    
    public static class Core
    {
        public static string RootFolder = new System.IO.FileInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).DirectoryName;
        public static string StoragePath = new System.IO.FileInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).DirectoryName + @"\servers\";

        public static Dictionary<int, MCServer> Servers = new Dictionary<int, MCServer> { };

        public static void StartUp()
        {
            //Clear out old files if they exist, if it doesn't work we'll just do it on next startup.
            try { if (File.Exists(RootFolder + @"\YAMS-Library.dll.OLD")) File.Delete(RootFolder + @"\YAMS-Library.dll.OLD"); }
            catch { };
            try { if (File.Exists(RootFolder + @"\YAMS-Service.exe.OLD")) File.Delete(RootFolder + @"\YAMS-Service.exe.OLD"); }
            catch { };
            try { if (File.Exists(RootFolder + @"\YAMS-Service.exe.config.OLD")) File.Delete(RootFolder + @"\YAMS-Service.exe.config.OLD"); }
            catch { };

            //Start DB Connection
            Database.init();
            Database.AddLog("Starting Up");

            //Is this the first run?
            if (Database.GetSetting("FirstRun", "YAMS") != "true") YAMS.Util.FirstRun();

            StoragePath = Database.GetSetting("StoragePath", "YAMS");

            Database.AddLog("Deleting and killing PIDs", "app", "debug");
            //Are there any PIDs we previously started still running?
            if (File.Exists(Core.RootFolder + "\\pids.txt"))
            {
                try
                {
                    StreamReader trPids = new StreamReader(Core.RootFolder + "\\pids.txt");
                    string line;
                    while ((line = trPids.ReadLine()) != null)
                    {
                        try
                        {
                            Process.GetProcessById(Convert.ToInt32(line)).Kill();
                        }
                        catch (Exception e)
                        {
                            Database.AddLog("Process " + line + " not killed: " + e.Message);
                        }
                    }
                   
                    trPids.Close();
                }
                catch (Exception e)
                {
                    Database.AddLog("Not all processes killed: " + e.Message);
                }
                try
                {
                    File.Delete(Core.RootFolder + "\\pids.txt");
                }
                catch (Exception e)
                {
                    Database.AddLog("Unable to delete the pids.txt file: " + e.Message);
                }
            };

            //Load any servers
            Database.AddLog("Reading minecraft servers!", "app", "debug");
            MySqlDataReader readerServers = Database.GetServers();
            ArrayList ServerIDs = new ArrayList();
            while (readerServers.Read())
            {
                int ServerID = Convert.ToInt32(readerServers.GetString("ServerID"));
                ServerIDs.Add(ServerID);
            }
            readerServers.Close();

            System.Collections.IEnumerator enu = ServerIDs.GetEnumerator();
            while (enu.MoveNext())
            {
                int ServerID = Convert.ToInt32(enu.Current);
                MCServer myServer = new MCServer(ServerID);
                Servers.Add(ServerID, myServer);
            }
            Database.AddLog("Starting web server!", "app", "debug");
            //Start Webserver
            WebServer.Init();
        }

        public static void ShutDown()
        {
            WebServer.Stop();
            foreach (KeyValuePair<int, MCServer> kvp in Core.Servers)
            {
                kvp.Value.Stop();
            }
            YAMS.Database.AddLog("Shutting Down");
        }

    }
}
