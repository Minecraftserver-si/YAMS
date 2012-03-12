using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using YAMS;
using System.Reflection;
using System.IO;
using MySql.Data.MySqlClient;

namespace YAMS_Gui
{
    public static class Program
    {
        public static YAMS.MCServer myServer;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {

            //Start DB Connection
            Database.init();
            Database.AddLog("Starting Up");

            //Is this the first run?
            if (Database.GetSetting("FirstRun", "YAMS") != "true") YAMS.Util.FirstRun();
            Database.SaveSetting("AdminPassword", "password");

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
                Core.Servers.Add(ServerID, myServer);
            }

            //Start Webserver
            WebServer.Init();
        
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new frmMain());
        }
    }
}
