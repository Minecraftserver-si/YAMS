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
                if ((WebSession.Current.UserName == "admin" || context.Request.Parameters["PSK"] == Database.GetSetting("PSK", "YAMS")))
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
                                            "\"vm\" : " + s.GetVMemory() + "}";
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

                            foreach (JObject option in jProps["options"])
                            {
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
                            break;
                        case "get-config-file":
                            List<string> listConfig = Core.Servers[Convert.ToInt32(context.Request.Parameters["serverid"])].ReadConfig(param["file"]);
                            strResponse = JsonConvert.SerializeObject(listConfig, Formatting.Indented);
                            break;
                        case "get-server-whitelist":
                            strResponse = "{ \"enabled\" : " + Core.Servers[Convert.ToInt32(context.Request.Parameters["serverid"])].GetProperty("white-list") + " }";
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
                                { "currentIP" , Database.GetSetting("YAMSListenIP", "YAMS") },
                                { "IPs" , JsonConvert.SerializeObject(listIPs, Formatting.None) }
                            };
                            strResponse = JsonConvert.SerializeObject(dicNetwork, Formatting.Indented).Replace(@"\", "").Replace("\"[", "[").Replace("]\"", "]");
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
                                YAMS.Database.AddLog("Invalid input on network settings: " + e.Message, "web", "warn");
                                return ProcessingResult.Abort;
                            }

                            Database.SaveSetting("EnablePortForwarding", param["portForwarding"]);
                            Database.SaveSetting("EnableOpenFirewall", param["openFirewall"]);
                            //Database.SaveSetting("EnablePublicSite", param["publicEnable"]);
                            Database.SaveSetting("AdminListenPort", param["adminPort"]);
                            //Database.SaveSetting("PublicListenPort", param["publicPort"]);
                            Database.SaveSetting("YAMSListenIP", param["listenIp"]);

                            Database.AddLog("Network settings have been saved, to apply changes a service restart is required. Please check they are correct before restarting", "web", "warn");
                            break;
                        case "logout":
                            WebSession.Current.UserName = "";
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
            else if (context.Request.Uri.AbsoluteUri.Equals(@"/"))
            {
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
