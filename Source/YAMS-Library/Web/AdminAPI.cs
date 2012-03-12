using System;
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
using Newtonsoft.Json.Linq;
using System.Data.SqlServerCe;
using HttpListener = HttpServer.HttpListener;
using YAMS;

namespace YAMS.Web
{
    public class AdminAPI : IModule
    {
        public ProcessingResult Process(RequestContext context)
        {
            int intServerID = 0;
            MCServer s;
            string json;
            JObject jProps;

            if (context.Request.Uri.AbsoluteUri.Contains(@"/api/"))
            {
                //must be authenticated

                //what is the action?
                if (context.Request.Method == Method.Post && (WebSession.Current.UserName == "admin" || context.Request.Parameters["PSK"] == Database.GetSetting("PSK", "YAMS")))
                {
                    String strResponse = "";
                    IParameterCollection param = context.Request.Parameters;
                    switch (context.Request.Parameters["action"])
                    {
                        case "log":
                            //grabs lines from the log.
                            int intStartID = Convert.ToInt32(context.Request.Parameters["start"]);
                            int intNumRows = Convert.ToInt32(context.Request.Parameters["rows"]);
                            int intServer = Convert.ToInt32(context.Request.Parameters["serverid"]);
                            string strLevel = context.Request.Parameters["level"];

                            DataSet ds = Database.ReturnLogRows(intStartID, intNumRows, strLevel, intServer);

                            strResponse = JsonConvert.SerializeObject(ds, Formatting.Indented);
                            break;
                        case "list":
                            //List available servers
                            strResponse = "{ \"servers\" : [";
                            foreach (KeyValuePair<int, MCServer> kvp in Core.Servers)
                            {
                                strResponse += "{ \"id\" : " + kvp.Value.ServerID + ", " +
                                                 "\"title\" : \"" + kvp.Value.ServerTitle + "\", " +
                                                 "\"ver\" : \"" + kvp.Value.ServerVersion + "\" } ,";
                            };
                            strResponse = strResponse.Remove(strResponse.Length - 1);
                            strResponse += "]}";
                            break;
                        case "status":
                            //Get status of a server
                            s = Core.Servers[Convert.ToInt32(context.Request.Parameters["serverid"])];
                            strResponse = "{ \"serverid\" : " + s.ServerID + "," +
                                            "\"status\" : \"" + s.Running + "\"," +
                                            "\"ram\" : " + s.GetMemory() + "," +
                                            "\"vm\" : " + s.GetVMemory() + "," +
                                            "\"restartneeded\" : \"" + s.RestartNeeded + "\"," +
                                            "\"restartwhenfree\" : \"" + s.RestartWhenFree + "\"," +
                                            "\"gamemode\" : \"" + s.GameMode + "\"," +
                                            "\"players\" : [";
                            if (s.Players.Count > 0)
                            {
                                foreach (KeyValuePair<string, Objects.Player> kvp in s.Players)
                                {
                                    Vector playerPos = kvp.Value.Position;
                                    strResponse += " { \"name\": \"" + kvp.Value.Username + "\", " +
                                                      "\"level\": \"" + kvp.Value.Level + "\", " +
                                                      "\"x\": \"" + playerPos.x.ToString("0.##") + "\", " +
                                                      "\"y\": \"" + playerPos.y.ToString("0.##") + "\", " +
                                                      "\"z\": \"" + playerPos.z.ToString("0.##") + "\" },";
                                };
                                strResponse = strResponse.Remove(strResponse.Length - 1);
                            }
                            strResponse += "]}";
                            break;
                        case "get-players":
                            DataSet dsPlayers = Database.GetPlayers(Convert.ToInt32(context.Request.Parameters["serverid"]));
                            JsonConvert.SerializeObject(dsPlayers, Formatting.Indented);
                            break;
                        case "overviewer":
                            //Maps a server
                            s = Core.Servers[Convert.ToInt32(context.Request.Parameters["serverid"])];
                            string strRenderModes = "";
                            if (param["normal"] == "true") strRenderModes += "normal";
                            if (param["lighting"] == "true")
                            {
                                if (strRenderModes != "") strRenderModes += ",";
                                strRenderModes += "lighting";
                            }
                            if (param["night"] == "true")
                            {
                                if (strRenderModes != "") strRenderModes += ",";
                                strRenderModes += "night";
                            }
                            if (param["spawn"] == "true")
                            {
                                if (strRenderModes != "") strRenderModes += ",";
                                strRenderModes += "spawn";
                            }
                            if (param["cave"] == "true")
                            {
                                if (strRenderModes != "") strRenderModes += ",";
                                strRenderModes += "cave";
                            }
                            AddOns.Overviewer over = new AddOns.Overviewer(s, "rendermodes=" + strRenderModes);
                            over.Start();
                            strResponse = "{ \"result\" : \"sent\" }";
                            break;
                        case "c10t":
                            //Images a server
                            s = Core.Servers[Convert.ToInt32(context.Request.Parameters["serverid"])];
                            AddOns.c10t c10t = new AddOns.c10t(s, "night=" + param["night"] + "&mode=" + param["mode"]);
                            c10t.Start();
                            strResponse = "{ \"result\" : \"sent\" }";
                            break;
                        case "tectonicus":
                            //Maps a server
                            s = Core.Servers[Convert.ToInt32(context.Request.Parameters["serverid"])];
                            AddOns.Tectonicus tecton = new AddOns.Tectonicus(s, "lighting=" + param["lighting"] + "&night=" + param["night"] + "&delete=" + param["delete"]);
                            tecton.Start();
                            strResponse = "{ \"result\" : \"sent\" }";
                            break;
                        case "start":
                            //Starts a server
                            Core.Servers[Convert.ToInt32(context.Request.Parameters["serverid"])].Start();
                            strResponse = "{ \"result\" : \"sent\" }";
                            break;
                        case "stop":
                            //Stops a server
                            Core.Servers[Convert.ToInt32(context.Request.Parameters["serverid"])].Stop();
                            strResponse = "{ \"result\" : \"sent\" }";
                            break;
                        case "restart":
                            //Restarts a server
                            Core.Servers[Convert.ToInt32(context.Request.Parameters["serverid"])].Restart();
                            strResponse = "{ \"result\" : \"sent\" }";
                            break;
                        case "delayed-restart":
                            //Restarts a server after a specified time and warns players
                            Core.Servers[Convert.ToInt32(context.Request.Parameters["serverid"])].DelayedRestart(Convert.ToInt32(param["delay"]));
                            strResponse = "{ \"result\" : \"sent\" }";
                            break;
                        case "restart-when-free":
                            Core.Servers[Convert.ToInt32(context.Request.Parameters["serverid"])].RestartIfEmpty();
                            strResponse = "{ \"result\" : \"sent\" }";
                            break;
                        case "command":
                            //Sends literal command to a server
                            Core.Servers[Convert.ToInt32(context.Request.Parameters["serverid"])].Send(context.Request.Parameters["message"]);
                            strResponse = "{ \"result\" : \"sent\" }";
                            break;
                        case "get-yams-settings":
                            DataSet dsSettings = Database.ReturnSettings();
                            JsonConvert.SerializeObject(dsSettings, Formatting.Indented);
                            break;
                        case "save-yams-settings":
                            //Settings update
                            foreach (Parameter p in param)
                            {
                                if (p.Name != "action") Database.SaveSetting(p.Name, p.Value);
                            }
                            break;
                        case "get-server-settings":
                            //retrieve all server settings as JSON
                            List<string> listIPsMC = new List<string>();
                            IPHostEntry ipListenMC = Dns.GetHostEntry("");
                            foreach (IPAddress ipaddress in ipListenMC.AddressList)
                            {
                                if (ipaddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) listIPsMC.Add(ipaddress.ToString());
                            }

                            intServerID = Convert.ToInt32(param["serverid"]);
                            strResponse = "{ \"serverid\" : " + intServerID + "," +
                                              "\"title\" : \"" + Database.GetSetting(intServerID, "ServerTitle") + "\"," +
                                              "\"optimisations\" : \"" + Database.GetSetting(intServerID, "ServerEnableOptimisations") + "\"," +
                                              "\"memory\" : \"" + Database.GetSetting(intServerID, "ServerAssignedMemory") + "\"," +
                                              "\"autostart\" : \"" + Database.GetSetting(intServerID, "ServerAutoStart") + "\"," +
                                              "\"type\" : \"" + Database.GetSetting(intServerID, "ServerType") + "\"," +
                                              "\"motd\" : \"" + Database.GetSetting("motd", "MC", intServerID) + "\"," +
                                              "\"listen\" : \"" + Core.Servers[Convert.ToInt32(context.Request.Parameters["serverid"])].GetProperty("server-ip") + "\"," +
                                              "\"port\" : \"" + Core.Servers[Convert.ToInt32(context.Request.Parameters["serverid"])].GetProperty("server-port") + "\"," +
                                              "\"IPs\": " + JsonConvert.SerializeObject(listIPsMC, Formatting.None);
                            strResponse += "}";
                            break;
                        case "get-server-connections":
                            intServerID = Convert.ToInt32(param["serverid"]);
                            strResponse = "{ \"dnsname\" : \"" + Database.GetSetting("DNSName", "YAMS") + "\", " +
                                            "\"externalip\" : \"" + Networking.GetExternalIP().ToString() + "\", " +
                                            "\"mcport\" : " + Core.Servers[Convert.ToInt32(context.Request.Parameters["serverid"])].GetProperty("server-port") + ", " +
                                            "\"publicport\" : " + Database.GetSetting("PublicListenPort", "YAMS") + " }";
                            break;
                        case "get-mc-settings":
                            //retrieve all server settings as JSON
                            intServerID = Convert.ToInt32(param["serverid"]);
                            
                            json = File.ReadAllText(YAMS.Core.RootFolder + @"\lib\properties.json");
                            jProps = JObject.Parse(json);

                            strResponse = "";
                            
                            foreach(JObject option in jProps["options"]) {
                                strResponse += "<p><label for=\"" + (string)option["key"] + "\" title=\"" + (string)option["description"] + "\">" + (string)option["name"] + "</label>";

                                string strValue = Core.Servers[Convert.ToInt32(context.Request.Parameters["serverid"])].GetProperty((string)option["key"]);

                                switch ((string)option["type"])
                                {
                                    case "string":
                                        strResponse += "<input type=\"text\" name=\"" + (string)option["key"] + "\" value=\"" + strValue + "\" />";
                                        break;
                                    case "boolean":
                                        strResponse += "<select name=\"" + (string)option["key"] + "\">";
                                        strResponse += "<option value=\"true\"";
                                        if (strValue == "true") strResponse += " selected";
                                        strResponse += ">True</option>";
                                        strResponse += "<option value=\"false\"";
                                        if (strValue == "false") strResponse += " selected";
                                        strResponse += ">False</option>";
                                        strResponse += "</select>";
                                        break;
                                    case "integer":
                                        strResponse += "<select name=\"" + (string)option["key"] + "\">";
                                        int intValue = Convert.ToInt32(strValue);
                                        for (var i = Convert.ToInt32((string)option["min"]); i <= Convert.ToInt32((string)option["max"]); i++)
                                        {
                                            strResponse += "<option value=\"" + i.ToString() + "\"";
                                            if (intValue == i) strResponse += " selected";
                                            strResponse += ">" + i.ToString() + "</option>";
                                        }
                                        strResponse += "</select>";
                                        break;
                                    case "array":
                                        strResponse += "<select name=\"" + (string)option["key"] + "\">";
                                        string strValues = (string)option["values"];
                                        string[] elements = strValues.Split(',');
                                        foreach (string values in elements)
                                        {
                                            string[] options = values.Split('|');
                                            strResponse += "<option value=\"" + options[0] + "\"";
                                            if (strValue == options[0]) strResponse += " selected";
                                            strResponse += ">" + options[1] + "</option>";
                                        }
                                        strResponse += "</select>";
                                        break;
                                }

                                strResponse += "</p>";
                            }

                            break;
                        case "save-server-settings":
                            intServerID = Convert.ToInt32(param["serverid"]);
                            Database.UpdateServer(intServerID, "ServerTitle", param["title"]);
                            Database.UpdateServer(intServerID, "ServerType", param["type"]);
                            Database.UpdateServer(intServerID, "ServerAssignedMemory", Convert.ToInt32(param["memory"]));
                            if (param["optimisations"] == "true") Database.UpdateServer(intServerID, "ServerEnableOptimisations", true);
                            else Database.UpdateServer(intServerID, "ServerEnableOptimisations", false);
                            if (param["autostart"] == "true") Database.UpdateServer(intServerID, "ServerAutoStart", true);
                            else Database.UpdateServer(intServerID, "ServerAutoStart", false);
                            Database.SaveSetting(intServerID, "motd", param["message"]);

                            //Save the server's MC settings
                            MCServer thisServer = Core.Servers[Convert.ToInt32(context.Request.Parameters["serverid"])];
                            thisServer.SaveProperty("server-ip", param["cfg_listen-ip"]);
                            thisServer.SaveProperty("server-port", param["cfg_port"]);

                            json = File.ReadAllText(YAMS.Core.RootFolder + @"\lib\properties.json");
                            jProps = JObject.Parse(json);

                            strResponse = "";

                            foreach (JObject option in jProps["options"])
                            {
                                thisServer.SaveProperty((string)option["key"], param[(string)option["key"]]);
                            }

                            if (thisServer.Running) thisServer.RestartIfEmpty();

                            break;
                        case "get-config-file":
                            List<string> listConfig = Core.Servers[Convert.ToInt32(context.Request.Parameters["serverid"])].ReadConfig(param["file"]);
                            strResponse = JsonConvert.SerializeObject(listConfig, Formatting.Indented);
                            break;
                        case "get-server-whitelist":
                            strResponse = "{ \"enabled\" : " + Core.Servers[Convert.ToInt32(context.Request.Parameters["serverid"])].GetProperty("white-list") + " }";
                            break;
                        case "upload-world":
                            var test = context.Request.Files["new-world"];
                            break;
                        case "delete-world":
                            bool bolRandomSeed = false;
                            if (param["randomseed"] == "true") bolRandomSeed = true;
                            Core.Servers[Convert.ToInt32(context.Request.Parameters["serverid"])].ClearWorld(bolRandomSeed);
                            strResponse = "{ \"result\" : \"sent\" }";
                            break;
                        case "remove-server":
                            Core.Servers[Convert.ToInt32(context.Request.Parameters["serverid"])].Stop();
                            Core.Servers.Remove(Convert.ToInt32(context.Request.Parameters["serverid"]));
                            Database.DeleteServer(Convert.ToInt32(context.Request.Parameters["serverid"]));
                            strResponse = "{ \"result\" : \"removed\" }";
                            break;
                        case "about":
                            Dictionary<string, string> dicAbout = new Dictionary<string, string> {
                                { "dll" , FileVersionInfo.GetVersionInfo(Path.Combine(Core.RootFolder, "YAMS-Library.dll")).FileVersion },
                                { "svc" , FileVersionInfo.GetVersionInfo(Path.Combine(Core.RootFolder, "YAMS-Service.exe")).FileVersion },
                                { "gui" , FileVersionInfo.GetVersionInfo(Path.Combine(Core.RootFolder, "YAMS-Updater.exe")).FileVersion },
                                { "db" , Database.GetSetting("DBSchema", "YAMS") }
                            };
                            strResponse = JsonConvert.SerializeObject(dicAbout, Formatting.Indented);
                            break;
                        case "installed-apps":
                            Dictionary<string, string> dicApps = new Dictionary<string, string> {
                                { "bukkit" , Database.GetSetting("BukkitInstalled", "YAMS") },
                                { "overviewer" , Database.GetSetting("OverviewerInstalled", "YAMS") },
                                { "c10t" , Database.GetSetting("C10tInstalled", "YAMS") },
                                { "biomeextractor" , Database.GetSetting("BiomeExtractorInstalled", "YAMS") },
                                { "tectonicus" , Database.GetSetting("TectonicusInstalled", "YAMS") },
                                { "nbtoolkit" , Database.GetSetting("NBToolkitInstalled", "YAMS") }
                            };
                            strResponse = JsonConvert.SerializeObject(dicApps, Formatting.Indented);
                            break;
                        case "update-apps":
                            Database.SaveSetting("OverviewerInstalled", param["overviewer"]);
                            Database.SaveSetting("C10tInstalled", param["c10t"]);
                            Database.SaveSetting("BiomeExtractorInstalled", param["biomeextractor"]);
                            Database.SaveSetting("BukkitInstalled", param["bukkit"]);
                            strResponse = "done";
                            break;
                        case "force-autoupdate":
                            AutoUpdate.CheckUpdates();
                            break;
                        case "network-settings":
                            List<string> listIPs = new List<string>();
                            IPHostEntry ipListen = Dns.GetHostEntry("");
                            foreach (IPAddress ipaddress in ipListen.AddressList)
                            {
                                if (ipaddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) listIPs.Add(ipaddress.ToString());
                            }

                            Dictionary<string, string> dicNetwork = new Dictionary<string, string> {
                                { "portForwarding" , Database.GetSetting("EnablePortForwarding", "YAMS") },
                                { "openFirewall" , Database.GetSetting("EnableOpenFirewall", "YAMS") },
                                { "publicEnable" , Database.GetSetting("EnablePublicSite", "YAMS") },
                                { "adminPort" , Database.GetSetting("AdminListenPort", "YAMS") },
                                { "publicPort" , Database.GetSetting("PublicListenPort", "YAMS") },
                                { "currentIP" , Database.GetSetting("YAMSListenIP", "YAMS") },
                                { "IPs" , JsonConvert.SerializeObject(listIPs, Formatting.None) }
                            };
                            strResponse = JsonConvert.SerializeObject(dicNetwork, Formatting.Indented).Replace(@"\","").Replace("\"[", "[").Replace("]\"", "]");
                            break;
                        case "save-network-settings":
                            int intTester = 0;
                            try
                            {
                                //Try to convert to integers to make sure something silly isn't put in. TODO: Javascript validation
                                intTester = Convert.ToInt32(param["adminPort"]);
                                intTester = Convert.ToInt32(param["publicPort"]);
                                IPAddress ipTest = IPAddress.Parse(param["listenIp"]);
                            }
                            catch (Exception e)
                            {
                                YAMS.Database.AddLog("Invalid input on network settings", "web", "warn");
                                return ProcessingResult.Abort;
                            }

                            Database.SaveSetting("EnablePortForwarding", param["portForwarding"]);
                            Database.SaveSetting("EnableOpenFirewall", param["openFirewall"]);
                            Database.SaveSetting("EnablePublicSite", param["publicEnable"]);
                            Database.SaveSetting("AdminListenPort", param["adminPort"]);
                            Database.SaveSetting("PublicListenPort", param["publicPort"]);
                            Database.SaveSetting("YAMSListenIP", param["listenIp"]);

                            Database.AddLog("Network settings have been saved, to apply changes a service restart is required. Please check they are correct before restarting", "web", "warn");
                            break;
                        case "job-list":
                            DataSet rdJobs = Database.ListJobs();
                            strResponse = JsonConvert.SerializeObject(rdJobs, Formatting.Indented);
                            break;
                        case "delete-job":
                            string strJobID = param["jobid"];
                            Database.DeleteJob(strJobID);
                            strResponse = "done";
                            break;
                        case "add-job":
                            intServerID = Convert.ToInt32(param["job-server"]);
                            int intHour = Convert.ToInt32(param["job-hour"]);
                            int intMinute = Convert.ToInt32(param["job-minute"]);
                            Database.AddJob(param["job-type"], intHour, intMinute, param["job-params"], intServerID);
                            break;
                        case "logout":
                            WebSession.Current.UserName = "";
                            break;
                        case "newserver":
                            var NewServer = new List<KeyValuePair<string, string>>();
                            NewServer.Add(new KeyValuePair<string, string>("motd", "Welcome to a YAMS server!"));
                            NewServer.Add(new KeyValuePair<string, string>("server-ip", Networking.GetListenIP().ToString()));
                            NewServer.Add(new KeyValuePair<string, string>("server-name", param["name"]));
                            NewServer.Add(new KeyValuePair<string, string>("server-port", Networking.TcpPort.FindNextAvailablePort(25565).ToString()));
                            Database.NewServerWeb(NewServer, param["name"], 1024);
                            strResponse = "done";
                            break;
                        case "updateDNS":
                            Database.SaveSetting("DNSName", param["dns-name"]);
                            Database.SaveSetting("DNSSecret", param["dns-secret"]);
                            Database.SaveSetting("LastExternalIP", param["dns-external"]);
                            strResponse = "done";
                            break;
                        case "getDNS":
                            strResponse = "{ \"name\":\"" + Database.GetSetting("DNSName", "YAMS") + "\", \"secret\": \"" + Database.GetSetting("DNSSecret", "YAMS") + "\", \"external\" : \"" + Networking.GetExternalIP().ToString() + "\" }";
                            break;
                        case "backup-now":
                            Backup.BackupNow(Core.Servers[Convert.ToInt32(param["serverid"])], param["title"]);
                            strResponse = "{ \"result\" : \"sent\" }";
                            break;
                        default:
                            return ProcessingResult.Abort;
                    }

                    context.Response.Reason = "Completed - YAMS";
                    context.Response.Connection.Type = ConnectionType.Close;
                    byte[] buffer = Encoding.UTF8.GetBytes(strResponse);
                    context.Response.Body.Write(buffer, 0, buffer.Length);
                }
                else
                {
                    // not a post, so say bye bye!
                    return ProcessingResult.Abort;
                }

                return ProcessingResult.SendResponse;
            }
            else if (context.Request.Uri.AbsoluteUri.Contains(@"/admin"))
            {

                if (WebSession.Current.UserName != "admin")
                {
                    context.Response.Reason = "Completed - YAMS";
                    context.Response.Connection.Type = ConnectionType.Close;
                    byte[] buffer = Encoding.UTF8.GetBytes(File.ReadAllText(YAMS.Core.RootFolder + @"\web\admin\login.html"));
                    context.Response.Body.Write(buffer, 0, buffer.Length);
                    return ProcessingResult.SendResponse;
                }
                else
                {
                    context.Response.Reason = "Completed - YAMS";
                    context.Response.Connection.Type = ConnectionType.Close;
                    byte[] buffer = Encoding.UTF8.GetBytes(File.ReadAllText(YAMS.Core.RootFolder + @"\web\admin\index.html"));
                    context.Response.Body.Write(buffer, 0, buffer.Length);
                    return ProcessingResult.SendResponse;
                }
            }
            else if (context.Request.Uri.AbsoluteUri.Contains(@"/login"))
            {
                //This is a login request, check it's legit
                string userName = context.Request.Form["strUsername"];
                string password = context.Request.Form["strPassword"];

                if (userName == "admin" && password == Database.GetSetting("AdminPassword", "YAMS"))
                {
                    WebSession.Create();
                    WebSession.Current.UserName = "admin";
                    context.Response.Redirect(@"/admin");
                    return ProcessingResult.SendResponse;
                }
                else
                {
                    context.Response.Reason = "Completed - YAMS";
                    context.Response.Connection.Type = ConnectionType.Close;
                    byte[] buffer = Encoding.UTF8.GetBytes(File.ReadAllText(YAMS.Core.RootFolder + @"\web\admin\login.html"));
                    context.Response.Body.Write(buffer, 0, buffer.Length);
                    return ProcessingResult.SendResponse;
                }
            }
            else if (context.Request.Uri.AbsoluteUri.Equals(@"/")) {
                    context.Response.Redirect(@"/admin");
                    return ProcessingResult.SendResponse;
            }
            else
            {
                return ProcessingResult.Abort;
            }

        }

    }
}
