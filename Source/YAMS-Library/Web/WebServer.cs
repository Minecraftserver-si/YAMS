﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Diagnostics;
using HttpServer;
using HttpServer.Authentication;
using HttpServer.Headers;
using HttpServer.Modules;
using HttpServer.Resources;
using HttpServer.Tools;
using Newtonsoft.Json;
using MySql.Data.MySqlClient;
using HttpListener = HttpServer.HttpListener;
using YAMS;

namespace YAMS
{
    public static class WebServer
    {
        private static Server adminServer;

        private static Thread adminServerThread;

        private static int AdminTryCount = 0;

        //Control
        public static void Init()
        {
            adminServer = new Server();

            //Handle the requests for static files
            var adminModule = new FileModule();
            adminModule.Resources.Add(new FileResources("/assets/", YAMS.Core.RootFolder + "\\web\\assets\\"));
            adminServer.Add(adminModule);
            //Handle requests to API
            adminServer.Add(new Web.AdminAPI());

            adminServer.Add(HttpListener.Create(IPAddress.Any, Convert.ToInt32(YAMS.Database.GetSetting("AdminListenPort", "YAMS"))));
            adminServer.ErrorPageRequested += new EventHandler<ErrorPageEventArgs>(myServer_ErrorPageRequested);
            adminServerThread = new Thread(new ThreadStart(StartAdmin));
            adminServerThread.Start();

            //Open firewall ports
            if (Database.GetSetting("EnableOpenFirewall", "YAMS") == "true")
            {
                Networking.OpenFirewallPort(Convert.ToInt32(YAMS.Database.GetSetting("AdminListenPort", "YAMS")), "Admin website");
            }
        }

        static void myServer_ErrorPageRequested(object sender, ErrorPageEventArgs e)
        {
            Database.AddLog(e.Exception.Message, "web", "error");
            e.Response.Reason = "Error - YAMS";
            e.Response.Connection.Type = ConnectionType.Close;
            byte[] buffer = Encoding.UTF8.GetBytes("<h1>500 Internal Server Error</h1><p>" + e.Exception.Message + "</p><p>" + e.Exception.Source + "</p><p>" + e.Exception.StackTrace + "</p><p>" + e.Exception.InnerException.StackTrace + "</p>");
            e.Response.Body.Write(buffer, 0, buffer.Length);
        }

        public static void StartAdmin()
        {
            try
            {
                while (Util.PortIsBusy(Convert.ToInt32(YAMS.Database.GetSetting("AdminListenPort", "YAMS"))) && AdminTryCount < 120)
                {
                    AdminTryCount++;
                    Database.AddLog("Admin Web server port still in use, attempt " + AdminTryCount, "web", "warn");
                    Thread.Sleep(5000);
                }

                adminServer.Start(5);
                //Start our session provider
                WebSession.Start(adminServer);
            }
            catch (System.Net.Sockets.SocketException e)
            {
                //Previous service has not released the port, so hang on and try again.
                Database.AddLog("Admin Web server port still in use, attempt " + AdminTryCount + ": " + e.Message, "web", "warn");
            }
            catch (Exception e)
            {
                EventLog myLog = new EventLog();
                myLog.Source = "YAMS";
                myLog.WriteEntry("Exception: " + e.Data, EventLogEntryType.Error);
            }

        }


        public static void Stop()
        {
            //Close firewall ports and forward via UPnP
            if (Database.GetSetting("EnableOpenFirewall", "YAMS") == "true")
            {
                Networking.CloseFirewallPort(Convert.ToInt32(YAMS.Database.GetSetting("AdminListenPort", "YAMS")));
                Networking.CloseFirewallPort(Convert.ToInt32(YAMS.Database.GetSetting("PublicListenPort", "YAMS")));
            }
            if (Database.GetSetting("EnablePortForwarding", "YAMS") == "true")
            {
                Networking.CloseUPnP(Convert.ToInt32(YAMS.Database.GetSetting("AdminListenPort", "YAMS")));
                Networking.CloseUPnP(Convert.ToInt32(YAMS.Database.GetSetting("PublicListenPort", "YAMS")));
            }

            adminServerThread.Abort();
        }

    }

    [Serializable]
    public class WebSession : Session
    {
        private static readonly SessionProvider<WebSession> _sessionProvider = new SessionProvider<WebSession>();

        static WebSession()
        {
            _sessionProvider.Cache = true;
        }

        /// <summary>
        /// Gets currently loaded session
        /// </summary>
        /// <remarks>
        /// Will not create sessions and manage new sessions, but returns a dummy one which is not handled by the provider class.
        /// Use the Create method to get a session that will be maintained by the provider class.
        /// </remarks>
        public static WebSession Current
        {
            get { return _sessionProvider.Current ?? new WebSession(); }
        }

        /// <summary>
        /// Gets or sets first name.
        /// </summary>
        public string FirstName { get; set; }

        /// <summary>
        /// Gets or sets user id.
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Gets or sets user name.
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// Gets or sets current errors.
        /// </summary>
        public static List<string> Errors { get; set; }

        /// <summary>
        /// Creates a new session and also sets it as the current one.
        /// </summary>
        /// <returns>Created session.</returns>
        public static WebSession Create()
        {
            return _sessionProvider.Create();
        }

        internal static void Start(Server webServer)
        {
            _sessionProvider.Start(webServer);
        }
    }


}
