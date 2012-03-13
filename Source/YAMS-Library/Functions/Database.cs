using System;
using System.Data;
using System.Text;
using System.IO;
using System.Collections.Generic;
using MySql.Data.MySqlClient;

namespace YAMS
{
    public class Database
    {
        private static MySqlConnection conn;

        private static DateTime defaultDateTime = new DateTime(1900, 1, 1);

        private static readonly object _lock = new object();

        public static void init()
        {
            //Open our DB connection for use all over the place
            conn = GetConnection();
            conn.Open();
            UpdateDB();
        }

        private static MySqlConnection GetConnection()
        {
            String MyConString = "Server=192.168.8.203;PORT=3306;" +
                "Database=yams;" +
                "Uid=yams;" +
                "Pwd=yams;";
            if (File.Exists(Core.RootFolder + "/mysql.txt"))
            {
                StreamReader reader = new StreamReader(Core.RootFolder + "/mysql.txt");
                MyConString = reader.ReadToEnd();
            }
            else
            {
                //File.Create(Core.RootFolder + "/mysql.txt");
                try
                {
                    TextWriter w = new StreamWriter(Core.RootFolder + "/mysql.txt");
                    w.WriteLine(MyConString);
                    w.Close();
                }
                catch (Exception e)
                {
                    Console.WriteLine("Can't write mysql.txt: " + e.Message);
                }
            }
            MySqlConnection connection = new MySqlConnection(MyConString);
            return connection;
        }

        public static DataSet ReturnLogRows(int intStartID = 0, int intNumRows = 0, string strLevels = "all", int intServerID = -1)
        {
            lock (_lock)
            {
                if (!conn.Ping())
                {
                    conn.Close();
                    init();
                }
                DataSet ds = new DataSet();
                MySqlCommand command = conn.CreateCommand();

                //We need to limit the number of rows or requests take an age and crash browsers
                if (intNumRows == 0) intNumRows = 1000;

                //Build our SQL
                StringBuilder strSQL = new StringBuilder();
                strSQL.Append("SELECT ");
                strSQL.Append("* FROM Log ");
                strSQL.Append("WHERE 1 ");
                if (intStartID > 0) strSQL.Append("AND LogID > " + intStartID.ToString() + " ");
                if (strLevels != "all") strSQL.Append("AND LogLevel = '" + strLevels + "' ");
                if (intServerID > -1) strSQL.Append("AND ServerID = " + intServerID.ToString() + " ");
                strSQL.Append("ORDER BY LogDateTime DESC, LogID ASC LIMIT 0," + intNumRows);

                command.CommandText = strSQL.ToString();
                MySqlDataAdapter adapter = new MySqlDataAdapter(command);
                adapter.Fill(ds);
                return ds;
            }
        }

        public static DataSet ReturnSettings()
        {
            lock (_lock)
            {
                if (!conn.Ping())
                {
                    conn.Close();
                    init();
                }
                DataSet ds = new DataSet();
                MySqlCommand command = conn.CreateCommand();

                command.CommandText = "SELECT * FROM YAMSSettings";
                MySqlDataAdapter adapter = new MySqlDataAdapter(command);
                adapter.Fill(ds);
                return ds;
            }
        }

        public static void AddLog(string strMessage, string strSource = "app", string strLevel = "info", bool bolSendToAdmin = false, int intServerID = 0)
        {
            lock (_lock)
            {
                if (!conn.Ping())
                {
                    conn.Close();
                    init();
                }
                if (strMessage == null) strMessage = "Null message received";

                string sqlIns = "INSERT INTO Log (LogSource, LogMessage, LogLevel, ServerID) VALUES (@source, @msg, @level, @serverid)";
                try
                {
                    if (strMessage.Length > 255) strMessage = strMessage.Substring(0, 255);
                    MySqlCommand cmdIns = new MySqlCommand(sqlIns, conn);
                    cmdIns.Parameters.AddWithValue("@source", strSource);
                    cmdIns.Parameters.AddWithValue("@msg", Util.Left(strMessage, 255));
                    cmdIns.Parameters.AddWithValue("@level", strLevel);
                    cmdIns.Parameters.AddWithValue("@serverid", intServerID);
                    cmdIns.ExecuteNonQuery();
                    cmdIns.Dispose();
                    cmdIns = null;

                }
                catch (Exception ex)
                {
                    throw new Exception(ex.ToString(), ex);
                }
            }
        }
        public static void AddLog(DateTime datTimeStamp, string strMessage, string strSource = "app", string strLevel = "info", bool bolSendToAdmin = false, int intServerID = 0)
        {
            lock (_lock)
            {
                if (!conn.Ping())
                {
                    conn.Close();
                    init();
                }
                if (strMessage == null) strMessage = "Null message received";

                string sqlIns = "INSERT INTO Log (LogSource, LogMessage, LogLevel, ServerID, LogDateTime) VALUES (@source, @msg, @level, @serverid, @timestamp)";
                try
                {
                    MySqlCommand cmdIns = new MySqlCommand(sqlIns, conn);
                    cmdIns.Parameters.AddWithValue("@source", strSource);
                    cmdIns.Parameters.AddWithValue("@msg", Util.Left(strMessage, 255));
                    cmdIns.Parameters.AddWithValue("@level", strLevel);
                    cmdIns.Parameters.AddWithValue("@serverid", intServerID);
                    cmdIns.Parameters.AddWithValue("@timestamp", datTimeStamp);

                    cmdIns.ExecuteNonQuery();
                    cmdIns.Dispose();
                    cmdIns = null;

                }
                catch (Exception ex)
                {
                    throw new Exception(ex.ToString(), ex);
                }
            }
        }

        // Returns the stored etag for the specified URL or blank string if no etag saved
        public static string GetEtag(string strURL)
        {
            lock (_lock)
            {
                if (!conn.Ping())
                {
                    conn.Close();
                    init();
                }
                try
                {
                    MySqlCommand cmd = new MySqlCommand("SELECT VersionETag FROM FileVersions WHERE VersionURL = @url", conn);
                    cmd.Parameters.AddWithValue("@url", strURL);
                    string eTag = (string)cmd.ExecuteScalar();
                    return eTag;
                }
                catch (Exception ex)
                {
                    AddLog("YAMS.Database.GetEtag Exception: " + ex.Message, "database", "error");
                    return "";
                }
            }
        }

        //Sets the Etag for a URL, replacing or adding the URL as needed
        public static bool SaveEtag(string strUrl, string strEtag)
        {
            lock (_lock)
            {
                if (!conn.Ping())
                {
                    conn.Close();
                    init();
                }
                MySqlCommand cmd = new MySqlCommand();
                cmd.Connection = conn;
                if (GetEtag(strUrl) == null)
                {
                    //Doesn't exist in DB already, so insert
                    cmd.CommandText = "INSERT INTO FileVersions (VersionURL, VersionETag) VALUES (@url, @etag);";
                }
                else
                {
                    //Exists, so need to update
                    cmd.CommandText = "UPDATE FileVersions SET VersionETag=@etag WHERE VersionURL=@url;";
                }
                cmd.Parameters.AddWithValue("@url", strUrl);
                cmd.Parameters.AddWithValue("@etag", strEtag);
                cmd.ExecuteNonQuery();
                return true;
            }
        }

        //Builds the server.properties file from current settings
        public static void BuildServerProperties(int intServerID)
        {
            lock (_lock)
            {
                if (!conn.Ping())
                {
                    conn.Close();
                    init();
                }
                StringBuilder sb = new StringBuilder();
                sb.Append("#Minecraft server properties: built by YAMS\n");
                //sb.Append("#Sun Nov 28 19:26:26 GMT 2010\n");

                MySqlCommand comProperties = new MySqlCommand("SELECT * FROM MCSettings WHERE ServerID = @serverid", conn);
                comProperties.Parameters.AddWithValue("@serverid", intServerID);
                MySqlDataReader readerProperties = null;
                readerProperties = comProperties.ExecuteReader();
                while (readerProperties.Read())
                {
                    sb.Append(readerProperties["SettingName"].ToString() + "=" + readerProperties["SettingValue"].ToString() + "\n");
                }
                //Save it as our update file in case the current is in use
                string strFile = @"\server.properties";
                if (File.Exists(Core.StoragePath + intServerID.ToString() + strFile)) strFile = @"\server.properties.UPDATE";
                File.WriteAllText(Core.StoragePath + intServerID.ToString() + strFile, sb.ToString());
                readerProperties.Close();
            }
        }

        //Get and set settings
        public static bool SaveSetting(string strSettingName, string strSettingValue)
        {
            lock (_lock)
            {
                if (!conn.Ping())
                {
                    conn.Close();
                    init();
                }
                MySqlCommand cmd = new MySqlCommand();
                cmd.Connection = conn;

                if (GetSetting(strSettingName, "YAMS") == null)
                {
                    //Doesn't exist in DB already, so insert
                    cmd.CommandText = "INSERT INTO YAMSSettings (SettingName, SettingValue) VALUES (@name, @value);";
                }
                else
                {
                    //Exists, so need to update
                    cmd.CommandText = "UPDATE YAMSSettings SET SettingValue=@value WHERE SettingName=@name;";
                }
                cmd.Parameters.AddWithValue("@name", strSettingName);
                cmd.Parameters.AddWithValue("@value", strSettingValue);
                cmd.ExecuteNonQuery();
                return true;
            }
        }
        public static bool SaveSetting(int intServerID, string strSettingName, string strSettingValue)
        {
            lock (_lock)
            {
                if (!conn.Ping())
                {
                    conn.Close();
                    init();
                }
                MySqlCommand cmd = new MySqlCommand();
                cmd.Connection = conn;

                if (GetSetting(strSettingName, "MC", intServerID) == null)
                {
                    //Doesn't exist in DB already, so insert
                    cmd.CommandText = "INSERT INTO MCSettings (SettingName, SettingValue, ServerID) VALUES (@name, @value, @id);";
                }
                else
                {
                    //Exists, so need to update
                    cmd.CommandText = "UPDATE MCSettings SET SettingValue=@value WHERE SettingName=@name AND ServerID=@id;";
                }
                cmd.Parameters.AddWithValue("@name", strSettingName);
                cmd.Parameters.AddWithValue("@value", strSettingValue);
                cmd.Parameters.AddWithValue("@id", intServerID);
                cmd.ExecuteNonQuery();
                return true;
            }
        }

        public static string GetSetting(string strSettingName, string strType, int intServerID = 0)
        {
            lock (_lock)
            {
                if (!conn.Ping())
                {
                    conn.Close();
                    init();
                }
                String strTableName = "";

                switch (strType)
                {
                    case "YAMS":
                        strTableName = "YAMSSettings";
                        break;
                    case "MC":
                        strTableName = "MCSettings";
                        break;
                }

                try
                {
                    MySqlCommand cmd = new MySqlCommand("SELECT SettingValue FROM " + strTableName + " WHERE SettingName = @name", conn);
                    cmd.Parameters.AddWithValue("@name", strSettingName);
                    string strSettingValue = (string)cmd.ExecuteScalar();
                    return strSettingValue;
                }
                catch (Exception ex)
                {
                    AddLog("YAMS.Database.GetSetting Exception: " + ex.Message, "database", "error");
                    return "";
                }
            }
        }

        public static object GetSetting(int intServerID, string strSettingName)
        {
            lock (_lock)
            {
                try
                {
                    if (!conn.Ping())
                    {
                        conn.Close();
                        init();
                    }
                    MySqlCommand cmd = new MySqlCommand("SELECT " + strSettingName + " FROM MCServers WHERE ServerID = @id", conn);
                    cmd.Parameters.AddWithValue("@id", intServerID);
                    var strSettingValue = cmd.ExecuteScalar();
                    return strSettingValue;
                }
                catch (Exception ex)
                {
                    AddLog("YAMS.Database.GetSetting Exception: " + ex.Message, "database", "error");
                    return "";
                }
            }
        }


        public static int NewServer(List<KeyValuePair<string, string>> listServer, string strServerTitle, int intServerMemory = 1024)
        {
            lock (_lock)
            {
                if (!conn.Ping())
                {
                    conn.Close();
                    init();
                }
                MySqlCommand cmd = new MySqlCommand();
                cmd.Connection = conn;

                //Create the server and get an ID
                cmd.CommandText = "INSERT INTO MCServers (ServerTitle, ServerWrapperMode, ServerAssignedMemory) VALUES (@title, 0, @mem)";
                cmd.Parameters.AddWithValue("@title", strServerTitle);
                cmd.Parameters.AddWithValue("@mem", intServerMemory);
                cmd.ExecuteNonQuery();
                cmd.Parameters.Clear();
                cmd.CommandText = "SELECT ServerID FROM MCServers WHERE ServerTitle = @title";
                cmd.Parameters.AddWithValue("@title", strServerTitle);
                MySqlDataReader r = cmd.ExecuteReader();
                int intNewID = 0;
                if (r.HasRows)
                {
                    r.Read();
                    intNewID = r.GetInt32("ServerID");
                    r.Close();
                }

                //Insert the settings into the DB for this server
                foreach (var element in listServer)
                {
                    cmd.Parameters.Clear();
                    cmd.CommandText = "INSERT INTO MCSettings (ServerID, SettingName, SettingValue) VALUES (@id, @name, @value);";
                    cmd.Parameters.AddWithValue("@id", intNewID);
                    cmd.Parameters.AddWithValue("@name", element.Key);
                    cmd.Parameters.AddWithValue("@value", element.Value);
                    cmd.ExecuteNonQuery();
                }

                //Set up Files + Folders
                if (!Directory.Exists(Core.StoragePath + intNewID.ToString())) Directory.CreateDirectory(Core.StoragePath + intNewID.ToString());
                //if (!Directory.Exists(Core.StoragePath + intNewID.ToString() + @"\config\")) Directory.CreateDirectory(Core.StoragePath + intNewID.ToString() + @"\config\");
                if (!Directory.Exists(Core.StoragePath + intNewID.ToString() + @"\world\")) Directory.CreateDirectory(Core.StoragePath + intNewID.ToString() + @"\world\");
                if (!Directory.Exists(Core.StoragePath + intNewID.ToString() + @"\renders\")) Directory.CreateDirectory(Core.StoragePath + intNewID.ToString() + @"\renders\");
                if (!Directory.Exists(Core.StoragePath + intNewID.ToString() + @"\renders\overviewer\")) Directory.CreateDirectory(Core.StoragePath + intNewID.ToString() + @"\renders\overviewer\");
                if (!Directory.Exists(Core.StoragePath + intNewID.ToString() + @"\renders\overviewer\output\")) Directory.CreateDirectory(Core.StoragePath + intNewID.ToString() + @"\renders\overviewer\output\");
                if (!File.Exists(Core.StoragePath + intNewID.ToString() + @"\banned-ips.txt")) File.Create(Core.StoragePath + intNewID.ToString() + @"\banned-ips.txt");
                if (!File.Exists(Core.StoragePath + intNewID.ToString() + @"\banned-players.txt")) File.Create(Core.StoragePath + intNewID.ToString() + @"\banned-players.txt");
                if (!File.Exists(Core.StoragePath + intNewID.ToString() + @"\ops.txt")) File.Create(Core.StoragePath + intNewID.ToString() + @"\ops.txt");
                if (!File.Exists(Core.StoragePath + intNewID.ToString() + @"\white-list.txt")) File.Create(Core.StoragePath + intNewID.ToString() + @"\white-list.txt");

                //Create default config files
                BuildServerProperties(intNewID);

                return intNewID;
            }
        }

        public static void DeleteServer(int intServerID)
        {
            lock (_lock)
            {
                if (!conn.Ping())
                {
                    conn.Close();
                    init();
                }
                MySqlCommand cmd = new MySqlCommand();
                cmd.Connection = conn;
                cmd.CommandText = "DELETE FROM MCServers WHERE ServerID = @id;";
                cmd.Parameters.AddWithValue("@id", intServerID);

                cmd.ExecuteNonQuery();
            }
        }

        public static int NewServerWeb(List<KeyValuePair<string, string>> listServer, string strServerTitle, int intServerMemory = 1024)
        {
            lock (_lock)
            {
                if (!conn.Ping())
                {
                    conn.Close();
                    init();
                }
                MySqlCommand cmd = new MySqlCommand();
                cmd.Connection = conn;

                //Create the server and get an ID
                cmd.CommandText = "INSERT INTO MCServers (ServerTitle, ServerWrapperMode, ServerAssignedMemory, ServerAutostart) VALUES (@title, 0, @mem, 0)";
                cmd.Parameters.AddWithValue("@title", strServerTitle);
                cmd.Parameters.AddWithValue("@mem", intServerMemory);
                cmd.ExecuteNonQuery();
                cmd.Parameters.Clear();
                cmd.CommandText = "SELECT ServerID FROM MCServers WHERE ServerTitle = @title";
                cmd.Parameters.AddWithValue("@title", strServerTitle);
                MySqlDataReader r = cmd.ExecuteReader();
                int intNewID = 0;
                if (r.HasRows)
                {
                    r.Read();
                    intNewID = r.GetInt32("ServerID");
                    r.Close();
                }

                //Set up Files + Folders
                if (!Directory.Exists(Core.StoragePath + intNewID.ToString())) Directory.CreateDirectory(Core.StoragePath + intNewID.ToString());
                //if (!Directory.Exists(Core.StoragePath + intNewID.ToString() + @"\config\")) Directory.CreateDirectory(Core.StoragePath + intNewID.ToString() + @"\config\");
                if (!Directory.Exists(Core.StoragePath + intNewID.ToString() + @"\world\")) Directory.CreateDirectory(Core.StoragePath + intNewID.ToString() + @"\world\");
                if (!Directory.Exists(Core.StoragePath + intNewID.ToString() + @"\backups\")) Directory.CreateDirectory(Core.StoragePath + intNewID.ToString() + @"\backups\");
                if (!Directory.Exists(Core.StoragePath + intNewID.ToString() + @"\renders\")) Directory.CreateDirectory(Core.StoragePath + intNewID.ToString() + @"\renders\");
                if (!Directory.Exists(Core.StoragePath + intNewID.ToString() + @"\renders\overviewer\")) Directory.CreateDirectory(Core.StoragePath + intNewID.ToString() + @"\renders\overviewer\");
                if (!Directory.Exists(Core.StoragePath + intNewID.ToString() + @"\renders\overviewer\output\")) Directory.CreateDirectory(Core.StoragePath + intNewID.ToString() + @"\renders\overviewer\output\");
                if (!File.Exists(Core.StoragePath + intNewID.ToString() + @"\banned-ips.txt")) File.Create(Core.StoragePath + intNewID.ToString() + @"\banned-ips.txt");
                if (!File.Exists(Core.StoragePath + intNewID.ToString() + @"\banned-players.txt")) File.Create(Core.StoragePath + intNewID.ToString() + @"\banned-players.txt");
                if (!File.Exists(Core.StoragePath + intNewID.ToString() + @"\ops.txt")) File.Create(Core.StoragePath + intNewID.ToString() + @"\ops.txt");
                if (!File.Exists(Core.StoragePath + intNewID.ToString() + @"\white-list.txt")) File.Create(Core.StoragePath + intNewID.ToString() + @"\white-list.txt");

                //Insert the settings into the DB for this server
                foreach (var element in listServer)
                {
                    cmd.Parameters.Clear();
                    cmd.CommandText = "INSERT INTO MCSettings (ServerID, SettingName, SettingValue) VALUES (@id, @name, @value);";
                    cmd.Parameters.AddWithValue("@id", intNewID);
                    cmd.Parameters.AddWithValue("@name", element.Key);
                    cmd.Parameters.AddWithValue("@value", element.Value);
                    cmd.ExecuteNonQuery();
                }

                //Create default config files
                BuildServerProperties(intNewID);

                //Add the server to the collection
                MCServer myServer = new MCServer(intNewID);
                Core.Servers.Add(intNewID, myServer);

                return intNewID;
            }
        }

        public static bool UpdateServer(int intServerID, string strSettingName, object strSettingValue)
        {
            lock (_lock)
            {
                if (!conn.Ping())
                {
                    conn.Close();
                    init();
                }
                MySqlCommand cmd = new MySqlCommand();
                cmd.Connection = conn;

                cmd.CommandText = "UPDATE MCServers SET " + strSettingName + "=@value WHERE ServerID=@id;";
                cmd.Parameters.AddWithValue("@value", strSettingValue);
                cmd.Parameters.AddWithValue("@id", intServerID);
                cmd.ExecuteNonQuery();
                return true;
            }
        }

        public static void UpdateDB()
        {
            switch (Convert.ToInt32(GetSetting("DBSchema", "YAMS")))
            {
                case 1:
                    //Update from Schema 1
                    Database.SaveSetting("StoragePath", Core.RootFolder + @"\servers\");
                    Database.SaveSetting("DBSchema", "2");
                    goto case 2;
                case 2:
                    //Update from Schema 2
                    Database.SaveSetting("UsageData", "true");
                    Database.SaveSetting("DBSchema", "3");
                    goto case 3;
                case 3:
                    Database.SaveSetting("EnablePortForwarding", "true");
                    Database.SaveSetting("EnableOpenFirewall", "true");
                    Database.SaveSetting("YAMSListenIP", Networking.GetListenIP().ToString());
                    Database.SaveSetting("DBSchema", "4");
                    goto case 4;
                //goto case 3; //etc
                case 4:
                    Database.SaveSetting("DNSName", "");
                    Database.SaveSetting("DNSSecret", "");
                    Database.SaveSetting("LastExternalIP", "");
                    Database.SaveSetting("DBSchema", "5");
                    goto case 5;
                case 5:
                    Database.SaveSetting("EnablePublicSite", "true");
                    Database.SaveSetting("DBSchema", "6");
                    goto case 6;
                case 6:

                    break;
                default:
                    break;
            }

        }

        public static MySqlDataReader GetServers()
        {
            lock (_lock)
            {
                if (!conn.Ping())
                {
                    conn.Close();
                    init();
                }
                MySqlCommand comServers = new MySqlCommand("SELECT * FROM MCServers;", conn);
                MySqlDataReader readerServers = comServers.ExecuteReader();
                return readerServers;
            }
        }

        //User Functions
        public static bool AddUser(string strUsername, int intServerID, string strLevel = "guest")
        {
            lock (_lock)
            {
                if (!conn.Ping())
                {
                    conn.Close();
                    init();
                }
                MySqlCommand cmd = new MySqlCommand();
                cmd.Connection = conn;
                cmd.CommandText = "INSERT INTO Players (PlayerName, PlayerServer, PlayerLevel) VALUES (@name, @server, @level);";
                cmd.Parameters.AddWithValue("@name", strUsername);
                cmd.Parameters.AddWithValue("@server", intServerID);
                cmd.Parameters.AddWithValue("@level", strLevel);
                cmd.ExecuteNonQuery();
                return true;
            }
        }

        public static string GetPlayerLevel(string strName, int intServerID)
        {
            lock (_lock)
            {
                if (!conn.Ping())
                {
                    conn.Close();
                    init();
                }
                try
                {
                    MySqlCommand cmd = new MySqlCommand("SELECT PlayerLevel FROM Players WHERE PlayerName = @name AND PlayerServer = @id", conn);
                    cmd.Parameters.AddWithValue("@id", intServerID);
                    cmd.Parameters.AddWithValue("@name", strName);
                    var strSettingValue = (string)cmd.ExecuteScalar();
                    return strSettingValue;
                }
                catch (Exception ex)
                {
                    AddLog("YAMS.Database.GetPlayerLevel Exception: " + ex.Message, "database", "error");
                    return "";
                }
            }
        }

        public static int GetPlayerCount(int intServerID)
        {
            lock (_lock)
            {
                if (!conn.Ping())
                {
                    conn.Close();
                    init();
                }
                try
                {
                    MySqlCommand cmd = new MySqlCommand("SELECT COUNT(PlayerID) AS Counter FROM Players WHERE PlayerServer = @id", conn);
                    cmd.Parameters.AddWithValue("@id", intServerID);
                    var intSettingValue = (int)cmd.ExecuteScalar();
                    return intSettingValue;
                }
                catch (Exception ex)
                {
                    AddLog("YAMS.Database.GetPlayerCount Exception: " + ex.Message, "database", "error");
                    return 0;
                }
            }
        }

        public static DataSet GetPlayers(int intServerID)
        {
            lock (_lock)
            {
                if (!conn.Ping())
                {
                    conn.Close();
                    init();
                }
                DataSet ds = new DataSet();
                MySqlCommand comPlayers = new MySqlCommand("SELECT * FROM Players WHERE PlayerServer = @id", conn);
                comPlayers.Parameters.AddWithValue("@id", intServerID);
                MySqlDataAdapter adapter = new MySqlDataAdapter(comPlayers);
                adapter.Fill(ds);
                return ds;
            }
        }

        public static void ClearLogs(string strPeriod, int intAmount)
        {
            lock (_lock)
            {
                if (!conn.Ping())
                {
                    conn.Close();
                    init();
                }
                MySqlCommand cmd = new MySqlCommand();
                cmd.Connection = conn;
                cmd.CommandText = "DELETE FROM Log WHERE LogDateTime < DATEADD(" + strPeriod + ", -" + intAmount + ", GETDATE());";
                cmd.ExecuteNonQuery();
            }
        }

        public static void ExecuteSQL(string strSQL)
        {
            lock (_lock)
            {
                if (!conn.Ping())
                {
                    conn.Close();
                    init();
                }
                MySqlCommand cmd = new MySqlCommand();
                cmd.Connection = conn;
                cmd.CommandText = strSQL;
                cmd.ExecuteNonQuery();
            }
        }

        ~Database()
        {
            conn.Close();
        }

    }
}
