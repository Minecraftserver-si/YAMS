﻿using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Timers;
using System.Net;
using System.Collections.Generic;
using YAMS;

namespace YAMS
{
    public class MCServer
    {
        public bool Running = false;
        private bool bolEnableJavaOptimisations = true;
        private int intAssignedMem = 1024;

        private string strWorkingDir = "";
        public string ServerDirectory;

        private Regex regRemoveDateStamp = new Regex(@"^([0-9]+\-[0-9]+\-[0-9]+ )");
        private Regex regRemoveTimeStamp = new Regex(@"^([0-9]+:[0-9]+:[0-9]+ )");
        private Regex regErrorLevel = new Regex(@"^\[([A-Z]+)\]{1}");
        private Regex regPlayerChat = new Regex(@"^(\<([\w-])+\>){1}");
        private Regex regConsoleChat = new Regex(@"^(\[CONSOLE\]){1}");
        private Regex regPlayerPM = new Regex(@"^(\[([\w])+\-\>(\w)+\]){1}");
        private Regex regPlayerLoggedIn = new Regex(@"^([\w]+)(?: \[\/[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+\:[0-9]+\] logged in with entity id)");
        private Regex regPlayerLoggedOut = new Regex(@"^([\w]+) ?(lost connection)");
        private Regex regServerVersion = new Regex(@"^(?:Starting minecraft server version )");
        private Regex regGameMode = new Regex(@"^(?:Default game type:) ([0-9])");

        public string ServerType = "vanilla";

        public Process prcMinecraft;

        private int intRestartSeconds = 0;
        private Timer timRestarter;

        public int ServerID;
        public string ServerVersion = "";
        public string ServerTitle = "";
        public bool HasChanged = false;
        public int PID;
        public int Port = 0;
        public string ListenIP = "";

        private bool SafeStop = false;

        public MCServer(int intServerID)
        {
            this.ServerID = intServerID;

            //Set this first so that we can use it right away
            this.ServerDirectory = Core.StoragePath + @"\" + this.ServerID.ToString();

            //Set this here to catch any old references to it.
            this.strWorkingDir = this.ServerDirectory;

            this.bolEnableJavaOptimisations = Convert.ToInt32(Database.GetSetting(this.ServerID, "ServerEnableOptimisations")) == 1;
            this.intAssignedMem = Convert.ToInt32(Database.GetSetting(this.ServerID, "ServerAssignedMemory"));
            this.ServerTitle = Convert.ToString(Database.GetSetting(this.ServerID, "ServerTitle"));
            this.ServerType = Convert.ToString(Database.GetSetting(this.ServerID, "ServerType"));
            //this.LogonMode = Convert.ToString(Database.GetSetting(this.ServerID, "ServerLogonMode"));
            this.ListenIP = this.GetProperty("server-ip");
            this.Port = Convert.ToInt32(this.GetProperty("server-port"));

        }

        public string GetProperty(string strPropertyName)
        {
            //File.WriteAllText(Core.RootFolder + @"\log.err", "Try to get property: " + strPropertyName + " from file: " + this.ServerDirectory + @"\server.properties");
            IniParser parser = new IniParser(this.ServerDirectory + @"\server.properties");
            return parser.GetSetting("ROOT", strPropertyName);
        }

        public void SaveProperty(string strPropertyName, string strPropertyValue)
        {
            //If there is already a partially updated file, we want to put this value in the new file
            string strPathToRead;
            if (File.Exists(this.ServerDirectory + @"\server.properties.UPDATE")) strPathToRead = this.ServerDirectory + @"\server.properties.UPDATE";
            else strPathToRead = this.ServerDirectory + @"\server.properties";
            IniParser parser = new IniParser(strPathToRead);
            parser.AddSetting("ROOT", strPropertyName, strPropertyValue);
            parser.SaveSettings(this.ServerDirectory + @"\server.properties.UPDATE", "#Minecraft server properties\r\n#Generated by YAMS " + DateTime.Now.ToString() + "\r\n");
        }

        public void Start()
        {
            if (this.Running) return;
            //Refresh Variables
            this.bolEnableJavaOptimisations = Convert.ToInt32(Database.GetSetting(this.ServerID, "ServerEnableOptimisations")) == 1;
            this.intAssignedMem = Convert.ToInt32(Database.GetSetting(this.ServerID, "ServerAssignedMemory"));
            this.ServerTitle = Convert.ToString(Database.GetSetting(this.ServerID, "ServerTitle"));
            this.ServerType = Convert.ToString(Database.GetSetting(this.ServerID, "ServerType"));
            //this.RestartWhenFree = false;
            //this.RestartNeeded = false;

            //What type of server are we running?
            string strFile = "";
            switch (this.ServerType)
            {
                case "vanilla":
                    strFile = "minecraft_server.jar";
                    break;
                case "bukkit":
                    strFile = "craftbukkit.jar";
                    break;
                case "pre":
                    strFile = "minecraft_server_pre.jar";
                    break;
                default:
                    strFile = "minecraft_server.jar";
                    break;
            }

            //First check if an update is waiting to be applied
            if (!Util.ReplaceFile(Core.RootFolder + "\\lib\\" + strFile, Core.RootFolder + "\\lib\\" + strFile + ".UPDATE")) return;

            //Also check if a new properties file is to be applied
            if (!Util.ReplaceFile(this.strWorkingDir + "server.properties", this.strWorkingDir + "server.properties.UPDATE")) return;

            this.prcMinecraft = new Process();

            try
            {
                var strArgs = "";
                var strFileName = YAMS.Util.JavaPath() + "java.exe";

                if (File.Exists(this.ServerDirectory + @"\args.txt"))
                {
                    StreamReader reader = new StreamReader(this.ServerDirectory + @"\args.txt");
                    String text = reader.ReadToEnd();
                    reader.Close();
                    strArgs = text;
                }
                else
                {
                    //If we have enabled the java optimisations add the additional
                    //arguments. See http://www.minecraftforum.net/viewtopic.php?f=1012&t=68128
                    if (bolEnableJavaOptimisations && YAMS.Util.HasJDK())
                    {
                        var intGCCores = Environment.ProcessorCount - 1;
                        if (intGCCores == 0) intGCCores = 1;
                        strArgs += "-server -XX:+UseConcMarkSweepGC -XX:+UseParNewGC -XX:+CMSIncrementalPacing -XX:ParallelGCThreads=" + intGCCores + " -XX:+AggressiveOpts";
                        strFileName = YAMS.Util.JavaPath("jdk") + "java.exe";
                    }

                    //Some specials for bukkit
                    if (this.ServerType == "bukkit")
                    {
                        strArgs += " -Djline.terminal=jline.UnsupportedTerminal";
                    }

                    //Basic arguments in all circumstances
                    strArgs += " -Xmx" + intAssignedMem + "M -Xms" + intAssignedMem + @"M -jar " + "\"" + Core.RootFolder + "\\lib\\";
                    strArgs += strFile;
                    strArgs += "\" nogui";
                }

                this.prcMinecraft.StartInfo.UseShellExecute = false;
                this.prcMinecraft.StartInfo.FileName = strFileName;
                this.prcMinecraft.StartInfo.Arguments = strArgs;
                this.prcMinecraft.StartInfo.CreateNoWindow = true;
                this.prcMinecraft.StartInfo.RedirectStandardError = true;
                this.prcMinecraft.StartInfo.RedirectStandardInput = true;
                this.prcMinecraft.StartInfo.RedirectStandardOutput = true;
                this.prcMinecraft.StartInfo.WorkingDirectory = this.strWorkingDir;

                //Set up events
                this.prcMinecraft.OutputDataReceived += new DataReceivedEventHandler(ServerOutput);
                this.prcMinecraft.ErrorDataReceived += new DataReceivedEventHandler(ServerError);
                this.prcMinecraft.EnableRaisingEvents = true;
                this.prcMinecraft.Exited += new EventHandler(ServerExited);

                //Finally start the thing
                this.prcMinecraft.Start();
                this.prcMinecraft.BeginOutputReadLine();
                this.prcMinecraft.BeginErrorReadLine();

                this.Running = true;
                this.SafeStop = false;
                Database.AddLog("Server Started: " + strArgs, "server", "info", false, this.ServerID);

                //Save the process ID so we can kill if there is a crash
                this.PID = this.prcMinecraft.Id;
                Util.AddPID(this.prcMinecraft.Id);
            }
            catch (Exception e)
            {
                Database.AddLog("Failed to start Server: " + e.Message, "library", "error", false, this.ServerID);
            }

        }

        public void Stop()
        {
            if (!Running) return;

            this.SafeStop = true;
            this.Send("stop");
            this.prcMinecraft.WaitForExit();
            this.prcMinecraft.CancelErrorRead();
            this.prcMinecraft.CancelOutputRead();
            Database.AddLog("Server Stopped", "server", "info", false, this.ServerID);
            this.Running = false;
        }

        public void Restart()
        {
            this.Stop();
            System.Threading.Thread.Sleep(10000);
            this.Start();
        }

        //Restart the server after specified number of seconds and warn users it's going to happen
        public void DelayedRestart(int intSeconds)
        {
            this.intRestartSeconds = intSeconds;
            this.timRestarter = new Timer();
            this.timRestarter.Interval = 1000; //Every second as we want to update the players
            this.timRestarter.Elapsed += new ElapsedEventHandler(RestarterTick);
            this.timRestarter.Enabled = true;
            Database.AddLog("AutoRestart initiated with " + intSeconds.ToString() + " second timer", "server", "info", false, this.ServerID);

        }
        private void RestarterTick(object source, ElapsedEventArgs e)
        {
            //How may seconds are left?  Send appropriate message
            if (this.intRestartSeconds > 100)
            {
                if (this.intRestartSeconds % 10 == 0) Send("say Server will restart in " + this.intRestartSeconds.ToString() + " seconds.");
            }
            else if (this.intRestartSeconds <= 100 && this.intRestartSeconds > 10)
            {
                if (this.intRestartSeconds % 5 == 0) Send("say Server will restart in " + this.intRestartSeconds.ToString() + " seconds.");
            }
            else if (this.intRestartSeconds <= 10 && this.intRestartSeconds > 0)
            {
                Send("say Server will restart in " + this.intRestartSeconds.ToString() + " seconds.");
            }
            else if (this.intRestartSeconds <= 0)
            {
                timRestarter.Enabled = false;
                this.Restart();
            }

            this.intRestartSeconds--;
        }
        public void CancelDelayedRestart()
        {
            this.timRestarter.Enabled = false;
            this.intRestartSeconds = 0;
            Database.AddLog("Delayed restart cancelled", "server", "info", false, this.ServerID);
        }

        //Send command to stdin on the server process
        public void Send(string strMessage)
        {
            if (!this.Running || this.prcMinecraft == null || this.prcMinecraft.HasExited) return;
            this.prcMinecraft.StandardInput.WriteLine(strMessage);
        }

        //Some shortcut commands
        public void Save()
        {
            this.Send("save-all");
            //Generally this needs a long wait
            System.Threading.Thread.Sleep(10000);
        }
        public void EnableSaving()
        {
            this.Send("save-on");
            //Generally this needs a long wait
            System.Threading.Thread.Sleep(10000);
        }
        public void DisableSaving()
        {
            this.Send("save-off");
            //Generally this needs a long wait
            System.Threading.Thread.Sleep(10000);
        }

        //Catch the output from the server process
        private void ServerOutput(object sender, DataReceivedEventArgs e) { if (e.Data != null && e.Data != ">") YAMS.Database.AddLog(DateTime.Now, e.Data, "server", "out", false, this.ServerID); }
        private void ServerError(object sender, DataReceivedEventArgs e)
        {
            DateTime datTimeStamp = DateTime.Now;

            //Catch null messages (usually as server is going down)
            if (e.Data == null || e.Data == ">") return;

            //MC's server seems to use stderr for things that aren't really errors, so we need some logic to catch that.
            string strLevel = "info";
            string strMessage = e.Data;
            //Strip out date and time info
            strMessage = this.regRemoveDateStamp.Replace(strMessage, "");
            strMessage = this.regRemoveTimeStamp.Replace(strMessage, "");

            //Work out the error level then remove it from the string
            Match regMatch = this.regErrorLevel.Match(strMessage);
            strMessage = this.regErrorLevel.Replace(strMessage, "").Trim();

            if (regMatch.Success)
            {
                switch (regMatch.Groups[1].Value)
                {
                    case "INFO":
                        //Check if it's player chat
                        if (regPlayerChat.Match(strMessage).Success || regPlayerPM.Match(strMessage).Success || regConsoleChat.Match(strMessage).Success) strLevel = "chat";
                        else strLevel = "info";
                        //See if it's the server version tag
                        if (regServerVersion.Match(strMessage).Success) this.ServerVersion = strMessage.Replace("Starting minecraft server version ", "");
                        break;
                    case "WARNING":
                        strLevel = "warn";
                        break;
                    default:
                        strLevel = "error";
                        break;
                }
            }
            else { strLevel = "error"; }

            if (strMessage.IndexOf("Invalid or corrupt jarfile ") > -1)
            {
                this.SafeStop = true;
            }
            Database.AddLog(datTimeStamp, strMessage, "server", strLevel, false, this.ServerID);
        }

        private void ServerExited(object sender, EventArgs e)
        {
            DateTime datTimeStamp = DateTime.Now;
            Database.AddLog(datTimeStamp, "Server Exited", "server", "warn", false, this.ServerID);
            this.Running = false;
            Util.RemovePID(this.PID);

            //Did the server stop safely?
            if (!this.SafeStop)
            {
                System.Threading.Thread.Sleep(10000);
                this.Start();
            }
        }

        //Returns the amount of RAM being used by this server
        public int GetMemory()
        {
            if (this.Running)
            {
                this.prcMinecraft.Refresh();
                return Convert.ToInt32(this.prcMinecraft.WorkingSet64 / (1024 * 1024));
            }
            else { return 0; }
        }

        //Returns the amount of Virtual Memory being used by the server
        public int GetVMemory()
        {
            if (this.Running)
            {
                this.prcMinecraft.Refresh();
                return Convert.ToInt32(this.prcMinecraft.VirtualMemorySize64 / (1024 * 1024));
            }
            else { return 0; }
        }

        //Read contents of a config file into a list
        public List<string> ReadConfig(string strFile)
        {
            List<string> lines = new List<string>();

            try
            {
                using (StreamReader r = new StreamReader(this.strWorkingDir + @"\" + strFile))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        if (line != "") lines.Add(line);
                    }
                }

                return lines;
            }
            catch (IOException e)
            {
                Database.AddLog("Exception reading config file " + strFile + ": " + e.Message, "web", "warn");
                return new List<string>();
            }
        }

    }
}
