using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using MySql.Data.MySqlClient;
using YAMS;

namespace YAMS
{
    public static class JobEngine
    {

        public static Timer timJob;



        public static void Init()
        {
            //Tick every minute
            timJob = new Timer(new TimerCallback(Tick), null, 0, 1 * 60 * 1000);
        }

        public static void Tick(object t)
        {
            DateTime datNow = DateTime.Now;
            int intMinutes = datNow.Minute;
            int intHour = datNow.Hour;

            //is it time to phone home?
            if (Database.GetSetting("UsageData", "YAMS") == "true" && intMinutes == 0) Util.PhoneHome();

            //Should we check DNS?
            if (intMinutes % 5 == 0 && Database.GetSetting("DNSName", "YAMS") != "") Util.UpdateDNS();

            //Get jobs for current minute
            MySqlDataReader rdJobs = Database.GetJobs(intHour, intMinutes);

            MCServer s;

            while (rdJobs.Read())
            {
                try
                {

                    //Split up the parameters
                    string strParams = rdJobs["JobParams"].ToString();
                    Dictionary<string, string> jobParams = new Dictionary<string, string> { };

                    if (strParams != "")
                    {
                        string[] arrKeys = strParams.Split('&');
                        foreach (string strKey in arrKeys)
                        {
                            string[] arrValues = strKey.Split('=');
                            if (arrValues.Length == 2)
                            {
                                jobParams.Add(arrValues[0], arrValues[1]);
                            }
                            else
                            {
                                Database.AddLog("Params failed on job. String was " + strParams, "job", "warn");
                            }
                        }
                    }

                    switch (rdJobs["JobAction"].ToString())
                    {
                        case "overviewer":
                            s = Core.Servers[Convert.ToInt32(rdJobs["JobServer"])];
                            AddOns.Overviewer gmap = new AddOns.Overviewer(s, rdJobs["JobParams"].ToString());
                            gmap.Start();
                            break;
                        case "c10t":
                            s = Core.Servers[Convert.ToInt32(rdJobs["JobServer"])];
                            AddOns.c10t c10t = new AddOns.c10t(s, rdJobs["JobParams"].ToString());
                            c10t.Start();
                            break;
                        case "biome-extractor":
                            s = Core.Servers[Convert.ToInt32(rdJobs["JobServer"])];
                            AddOns.BiomeExtractor extractor = new AddOns.BiomeExtractor(s, rdJobs["JobParams"].ToString());
                            extractor.Start();
                            break;
                        case "backup":
                            s = Core.Servers[Convert.ToInt32(rdJobs["JobServer"])];
                            Backup.BackupIfNeeded(s);
                            break;
                        case "update":
                            AutoUpdate.CheckUpdates();
                            break;
                        case "delayedrestart":
                            s = Core.Servers[Convert.ToInt32(rdJobs["JobServer"])];
                            s.DelayedRestart(Convert.ToInt32(rdJobs["JobParams"]));
                            break;
                        case "restartwhenfree":
                            s = Core.Servers[Convert.ToInt32(rdJobs["JobServer"])];
                            s.RestartIfEmpty();
                            break;
                        case "clearlogs":
                            Database.ClearLogs(jobParams["period"], Convert.ToInt32(jobParams["amount"]));
                            break;
                        case "clearbackups":
                            Backup.ClearBackups(Core.Servers[Convert.ToInt32(rdJobs["JobServer"])], jobParams["period"], Convert.ToInt32(jobParams["amount"]));
                            break;
                        default:
                            Database.AddLog("Invalid entry in Job database", "job", "warn");
                            break;
                    }

                } catch (Exception e) {
                    YAMS.Database.AddLog("Error running job: " + e.Message, "job", "error");
                }
            } 
        
        }
    }
}
