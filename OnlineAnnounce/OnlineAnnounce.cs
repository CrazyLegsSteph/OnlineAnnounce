using Mono.Data.Sqlite;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Timers;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;

namespace OnlineAnnounce
{
    public class OAPlayer
    {
        public string greet;
        public string leave;
        public Color greetRGB;
        public Color leaveRGB;

        public string userAccountName;
        public int userID;
        public bool pastGreet;
        public bool hasGreeted;

        public OAPlayer(bool _isGreet, string _announcement, string _userAccountName, int _userID)
        {
            if (_isGreet)
            {
                greet = _announcement;
                leave = null;
            }
            else
            {
                greet = null;
                leave = _announcement;
            }

            userAccountName = _userAccountName;
            userID = _userID;
            greetRGB = new Color(OnlineAnnounce.config.defaultR, OnlineAnnounce.config.defaultG, OnlineAnnounce.config.defaultB);
            leaveRGB = new Color(OnlineAnnounce.config.defaultR, OnlineAnnounce.config.defaultG, OnlineAnnounce.config.defaultB);
            pastGreet = false;
            hasGreeted = false;
        }

        public OAPlayer(string _greet, string _leave, string _userAccountName, int _userID, Color _greetRGB, Color _leaveRGB)
        {
            greet = _greet;
            leave = _leave;
            userAccountName = _userAccountName;
            userID = _userID;
            greetRGB = _greetRGB;
            leaveRGB = _leaveRGB;
            pastGreet = false;
            hasGreeted = false;
        }

        public void Greet(int index)
        {
            TSPlayer.All.SendMessage(string.Format("[{0}] {1}", userAccountName, greet), greetRGB);
            TShock.Players[index].SendMessage(string.Format("[{0}] {1}", userAccountName, greet), greetRGB);
            hasGreeted = true;
        }

        public void Leave()
        {
            TSPlayer.All.SendMessage(string.Format("[{0}] {1}", userAccountName, leave), leaveRGB);
        }
    }

    [ApiVersion(1, 16)]
    public class OnlineAnnounce : TerrariaPlugin
    {
        public override string Name { get { return "OnlineAnnounce"; } }
        public override string Author { get { return "Zaicon"; } }
        public override string Description { get { return "Broadcasts an custom announcement upon player join/leave."; } }
        public override Version Version { get { return new Version(4, 3, 1, 3); } }

        private static IDbConnection db;
        public static Config config = new Config();
        public string configPath = Path.Combine(TShock.SavePath, "OnlineAnnounceConfig.json");
        private Dictionary<int, OAPlayer> players = new Dictionary<int, OAPlayer>();
        public static Dictionary<int, int> indexid = new Dictionary<int, int>();

        public OnlineAnnounce(Main game)
            : base(game)
        {
            base.Order = -1;
        }

        #region Initialize/Dispose
        public override void Initialize()
        {
            ServerApi.Hooks.GameInitialize.Register(this, OnInitialize);
            ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
            ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreet);
            GeneralHooks.ReloadEvent += OnReload;
            PlayerHooks.PlayerPostLogin += OnPostLogin;
        }

        protected override void Dispose(bool Disposing)
        {
            if (Disposing)
            {
                ServerApi.Hooks.GameInitialize.Deregister(this, OnInitialize);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
                ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreet);
                GeneralHooks.ReloadEvent -= OnReload;
                PlayerHooks.PlayerPostLogin -= OnPostLogin;
            }
            base.Dispose(Disposing);
        }
        #endregion

        #region Hooks
        private void OnInitialize(EventArgs args)
        {
            DBConnect();
            loadConfig();
            readDatabase();

            Commands.ChatCommands.Add(new Command("oa.greet", UGreet, "greet"));
            Commands.ChatCommands.Add(new Command("oa.leave", ULeave, "leave"));
            Commands.ChatCommands.Add(new Command("oa.purge", UPurge, "purgeannouncements", "pa"));
        }

        private void OnGreet(GreetPlayerEventArgs args)
        {
            if (TShock.Players[args.Who] == null)
                return;

            int id = TShock.Players[args.Who].UserID;

            if (!indexid.ContainsKey(args.Who))
                indexid.Add(args.Who, id);

            if (players.ContainsKey(id))
            {
                if (!players[id].hasGreeted)
                {
                    players[id].Greet(args.Who);
                }

                players[id].pastGreet = true;
            }
        }

        private void OnPostLogin(PlayerPostLoginEventArgs args)
        {
            int id = args.Player.UserID;

            if (!indexid.ContainsKey(args.Player.Index))
                indexid.Add(args.Player.Index, args.Player.UserID);
            else
                indexid[args.Player.Index] = id;

            if (players.ContainsKey(id))
            {
                if (players[id].pastGreet)
                {
                    players[id].Greet(args.Player.Index);
                }
            }
        }

        private void OnReload(ReloadEventArgs args)
        {
            loadConfig();
            readDatabase();
        }

        private void OnLeave(LeaveEventArgs args)
        {
            int id = -1;

            if (indexid.ContainsKey(args.Who))
                id = indexid[args.Who];
            else
                return;

            if (id != -1)
            {
                players[id].Leave();

                players[id].pastGreet = false;
                players[id].hasGreeted = false;
            }

            indexid.Remove(args.Who);
        }
        #endregion

        #region Greet commands
        private void UGreet(CommandArgs args)
        {
            if (args.Parameters.Count > 2 && args.Parameters[0].ToLower() == "set")
            {
                TShockAPI.DB.User player = TShock.Users.GetUserByName(args.Parameters[1]);

                if (player == null)
                {
                    args.Player.SendErrorMessage("User doesn't exist: {0}", args.Parameters[1]);
                    return;
                }

                if (player.Name != args.Player.UserAccountName && !args.Player.Group.HasPermission("oa.mod"))
                {
                    args.Player.SendErrorMessage("You do not have permission to change other players' greeting announcement.");
                    return;
                }

                List<string> param = args.Parameters;

                param.RemoveAt(0); //Remove 'set'
                param.RemoveAt(0); //Remove player name

                string announcement = string.Join(" ", param.Select(p => p));

                foreach (string badword in config.badwords)
                {
                    if (announcement.Contains(badword) && !args.Player.Group.HasPermission("oa.mod"))
                    {
                        args.Player.SendErrorMessage("You may not use the phrase {0} in this greeting announcement!", badword);
                        return;
                    }
                }

                setAnnounce(true, player.ID, player.Name, announcement);
                args.Player.SendSuccessMessage("{0} greeting announcement has been set to:", (player.Name == args.Player.UserAccountName ? "Your" : (player.Name + "'s")));
                args.Player.SendMessage(string.Format("[{0}] {1}", player.Name, players[player.ID].greet), players[player.ID].greetRGB);

                return;
            }

            if (args.Parameters.Count == 2 && args.Parameters[0].ToLower() == "remove")
            {
                TShockAPI.DB.User player = TShock.Users.GetUserByName(args.Parameters[1]);

                if (player == null)
                {
                    args.Player.SendErrorMessage("User doesn't exist: {0}", args.Parameters[1]);
                    return;
                }

                if (player.Name != args.Player.UserAccountName && !args.Player.Group.HasPermission("oa.mod"))
                {
                    args.Player.SendErrorMessage("You do not have permission to remove other players' greeting announcement.");
                    return;
                }

                if (!players.ContainsKey(player.ID) || players[player.ID].greet == null)
                {
                    args.Player.SendErrorMessage("This player doesn't have a greeting announcement to remove!");
                    return;
                }

                removeAnnounce(true, player.ID);
                args.Player.SendSuccessMessage("{0} greeting announcement has been removed.", (player.Name == args.Player.UserAccountName ? "Your" : (player.Name + "'s")));

                return;
            }

            if (args.Parameters.Count > 2 && args.Parameters[0].ToLower() == "color")
            {
                if (!args.Player.Group.HasPermission("oa.mod"))
                {
                    args.Player.SendErrorMessage("You do not have permission to change greeting announcement colors.");
                    return;
                }

                TShockAPI.DB.User player = TShock.Users.GetUserByName(args.Parameters[1]);

                if (player == null)
                {
                    args.Player.SendErrorMessage("User doesn't exist: {0}", args.Parameters[1]);
                    return;
                }

                if (!players.ContainsKey(player.ID) || players[player.ID].greet == null)
                {
                    args.Player.SendErrorMessage("This player doesn't have a greeting announcement to modify the color of!");
                    return;
                }

                Color color;

                List<string> param = args.Parameters;

                param.RemoveAt(0); //Remove 'color'
                param.RemoveAt(0); //Remove player name

                if (!tryParseColor(param, out color))
                {
                    args.Player.SendErrorMessage("Invalid color syntax: {0}greet color <player> rrr,ggg,bbb OR {0}greet color <player> rrr ggg bbb", TShock.Config.CommandSpecifier);
                    return;
                }

                setColor(true, player.ID, color);
                args.Player.SendSuccessMessage("{0} greeting announcement has been set to:", (player.Name == args.Player.UserAccountName ? "Your" : (player.Name + "'s")));
                args.Player.SendMessage(string.Format("[{0}] {1}", player.Name, players[player.ID].greet), players[player.ID].greetRGB);

                return;
            }

            args.Player.SendErrorMessage("Invalid syntax:");
            args.Player.SendErrorMessage("{0}greet set <player> <announcement>", TShock.Config.CommandSpecifier);
            args.Player.SendErrorMessage("{0}greet remove <player>", TShock.Config.CommandSpecifier);
            if (args.Player.Group.HasPermission("oa.mod"))
                args.Player.SendErrorMessage("{0}greet color <player> <rgb>", TShock.Config.CommandSpecifier);
        }

        private void ULeave(CommandArgs args)
        {
            if (args.Parameters.Count > 2 && args.Parameters[0].ToLower() == "set")
            {
                TShockAPI.DB.User player = TShock.Users.GetUserByName(args.Parameters[1]);

                if (player == null)
                {
                    args.Player.SendErrorMessage("User doesn't exist: {0}", args.Parameters[1]);
                    return;
                }

                if (player.Name != args.Player.UserAccountName && !args.Player.Group.HasPermission("oa.mod"))
                {
                    args.Player.SendErrorMessage("You do not have permission to change other players' leaving announcement.");
                    return;
                }

                List<string> param = args.Parameters;

                param.RemoveAt(0); //Remove 'set'
                param.RemoveAt(0); //Remove player name

                string announcement = string.Join(" ", param.Select(p => p));

                foreach (string badword in config.badwords)
                {
                    if (announcement.Contains(badword) && !args.Player.Group.HasPermission("oa.mod"))
                    {
                        args.Player.SendErrorMessage("You may not use the phrase {0} in this leaving announcement!", badword);
                        return;
                    }
                }

                setAnnounce(false, player.ID, player.Name, announcement);
                args.Player.SendSuccessMessage("{0} leaving announcement has been set to:", (player.Name == args.Player.UserAccountName ? "Your" : (player.Name + "'s")));
                args.Player.SendMessage(string.Format("[{0}] {1}", player.Name, players[player.ID].leave), players[player.ID].leaveRGB);

                return;
            }

            if (args.Parameters.Count == 2 && args.Parameters[0].ToLower() == "remove")
            {
                TShockAPI.DB.User player = TShock.Users.GetUserByName(args.Parameters[1]);

                if (player == null)
                {
                    args.Player.SendErrorMessage("User doesn't exist: {0}", args.Parameters[1]);
                    return;
                }

                if (player.Name != args.Player.UserAccountName && !args.Player.Group.HasPermission("oa.mod"))
                {
                    args.Player.SendErrorMessage("You do not have permission to remove other players' leaving announcement.");
                    return;
                }

                if (!players.ContainsKey(player.ID) || players[player.ID].leave == null)
                {
                    args.Player.SendErrorMessage("This player doesn't have a leaving announcement to remove!");
                    return;
                }

                removeAnnounce(false, player.ID);
                args.Player.SendSuccessMessage("{0} leaving announcement has been removed.", (player.Name == args.Player.UserAccountName ? "Your" : (player.Name + "'s")));

                return;
            }

            if (args.Parameters.Count > 2 && args.Parameters[0].ToLower() == "color")
            {
                if (!args.Player.Group.HasPermission("oa.mod"))
                {
                    args.Player.SendErrorMessage("You do not have permission to change leaving announcement colors.");
                    return;
                }

                TShockAPI.DB.User player = TShock.Users.GetUserByName(args.Parameters[1]);

                if (player == null)
                {
                    args.Player.SendErrorMessage("User doesn't exist: {0}", args.Parameters[1]);
                    return;
                }

                if (!players.ContainsKey(player.ID) || players[player.ID].greet == null)
                {
                    args.Player.SendErrorMessage("This player doesn't have a greeting announcement to modify the color of!");
                    return;
                }

                Color color;

                List<string> param = args.Parameters;

                param.RemoveAt(0); //Remove 'color'
                param.RemoveAt(0); //Remove player name

                if (!tryParseColor(param, out color))
                {
                    args.Player.SendErrorMessage("Invalid color syntax: {0}leave color <player> rrr,ggg,bbb OR {0}leave color <player> rrr ggg bbb", TShock.Config.CommandSpecifier);
                    return;
                }

                setColor(false, player.ID, color);
                args.Player.SendSuccessMessage("{0} leaving announcement has been set to:", (player.Name == args.Player.UserAccountName ? "Your" : (player.Name + "'s")));
                args.Player.SendMessage(string.Format("[{0}] {1}", player.Name, players[player.ID].leave), players[player.ID].leaveRGB);

                return;
            }

            args.Player.SendErrorMessage("Invalid syntax:");
            args.Player.SendErrorMessage("{0}leave set <player> <announcement>", TShock.Config.CommandSpecifier);
            args.Player.SendErrorMessage("{0}leave remove <player>", TShock.Config.CommandSpecifier);
            if (args.Player.Group.HasPermission("oa.mod"))
                args.Player.SendErrorMessage("{0}leave color <player> <rgb>", TShock.Config.CommandSpecifier);
        }

        private void UPurge(CommandArgs args)
        {
            if (args.Parameters.Count == 1 && args.Parameters[0].ToLower() == "confirm")
            {
                purgeDatabase();
                args.Player.SendSuccessMessage("Players with empty announcements have been purged from the database.");
                return;
            }
            args.Player.SendErrorMessage("This command will remove any database entries where any given player has no greeting AND no leaving announcements.");
            args.Player.SendErrorMessage("Use '/pa confirm' to activate this command.");
        }
        #endregion

        #region Internal Methods

        private bool tryParseColor(List<string> ucolor, out Color color)
        {
            color = new Color();

            if (ucolor.Count != 1 && ucolor.Count != 3)
                return false;

            string[] rgb = new string[] { "-1", "-1", "-1" };

            if (ucolor.Count == 1)
            {
                rgb = ucolor[0].Split(',');
                if (rgb.Length != 3)
                    return false;
            }
            if (ucolor.Count == 3)
            {
                rgb = new string[] { ucolor[0], ucolor[1], ucolor[2] };
            }

            int r = -1;
            int g = -1;
            int b = -1;

            if (!int.TryParse(rgb[0], out r) || !int.TryParse(rgb[1], out g) || !int.TryParse(rgb[2], out b))
                return false;

            if (r < 0 || r > 255)
                return false;
            if (g < 0 || g > 255)
                return false;
            if (b < 0 || b > 255)
                return false;

            color = new Color(r, g, b);

            return true;
        }

        private string colorToString(Color color)
        {
            return string.Format("{0},{1},{2}", color.R.ToString(), color.G.ToString(), color.B.ToString());
        }
        #endregion

        private void loadConfig()
        {
            (config = Config.Read(configPath)).Write(configPath);
        }
        #region Database Methods

        private void DBConnect()
        {
            switch (TShock.Config.StorageType.ToLower())
            {
                case "mysql":
                    string[] dbHost = TShock.Config.MySqlHost.Split(':');
                    db = new MySqlConnection()
                    {
                        ConnectionString = string.Format("Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4};",
                            dbHost[0],
                            dbHost.Length == 1 ? "3306" : dbHost[1],
                            TShock.Config.MySqlDbName,
                            TShock.Config.MySqlUsername,
                            TShock.Config.MySqlPassword)

                    };
                    break;

                case "sqlite":
                    string sql = Path.Combine(TShock.SavePath, "tshock.sqlite");
                    db = new SqliteConnection(string.Format("uri=file://{0},Version=3", sql));
                    break;

            }

            SqlTableCreator sqlcreator = new SqlTableCreator(db, db.GetSqlType() == SqlType.Sqlite ? (IQueryBuilder)new SqliteQueryCreator() : new MysqlQueryCreator());

            sqlcreator.EnsureTableStructure(new SqlTable("onlineannounce",
                new SqlColumn("userid", MySqlDbType.Int32) { Primary = true, Unique = true, Length = 6 },
                new SqlColumn("greet", MySqlDbType.Text) { Length = 100 },
                new SqlColumn("leaving", MySqlDbType.Text) { Length = 100 },
                new SqlColumn("greetrgb", MySqlDbType.Text) { Length = 11, NotNull = true },
                new SqlColumn("leavergb", MySqlDbType.Text) { Length = 11, NotNull = true }));
        }

        private void readDatabase()
        {
            players.Clear();

            using (QueryResult reader = db.QueryReader(@"SELECT * FROM onlineannounce;"))
            {
                while (reader.Read())
                {
                    string uan = TShock.Users.GetUserByID(reader.Get<int>("userid")).Name;

                    Color greetrgb;
                    Color leavergb;
                    tryParseColor(new List<string>() { reader.Get<string>("greetrgb") }, out greetrgb);
                    tryParseColor(new List<string>() { reader.Get<string>("leavergb") }, out leavergb);
                    players.Add(reader.Get<int>("userid"), new OAPlayer(reader.Get<string>("greet"), reader.Get<string>("leaving"), uan, reader.Get<int>("userid"), greetrgb, leavergb));
                }
            }
        }

        private int purgeDatabase()
        {
            List<int> delUserID = new List<int>();

            foreach (KeyValuePair<int, OAPlayer> kvp in players)
            {
                if (kvp.Value.greet == null && kvp.Value.leave == null)
                    delUserID.Add(kvp.Key);
            }

            int count = 0;

            foreach (int id in delUserID)
            {
                players.Remove(id);
                db.Query("DELETE FROM onlineannounce WHERE userid=@0;", id);
                count++;
            }

            return count;
        }

        private void setAnnounce(bool isGreeting, int userid, string useraccountname, string announcement)
        {
            if (players.ContainsKey(userid))
            {
                if (isGreeting)
                {
                    players[userid].greet = announcement;
                    db.Query("UPDATE onlineannounce SET greet=@0 WHERE userid = @1;", announcement, userid.ToString());
                }
                else
                {
                    players[userid].leave = announcement;
                    db.Query("UPDATE onlineannounce SET leaving=@0 WHERE userid = @1;", announcement, userid.ToString());
                }
            }
            else
            {

                if (isGreeting)
                {
                    players.Add(userid, new OAPlayer(true, announcement, useraccountname, userid));
                    db.Query("INSERT INTO onlineannounce (userid, greet, greetrgb, leavergb) VALUES (@0, @1, @2, @3);", userid.ToString(), announcement, colorToString(players[userid].greetRGB), colorToString(players[userid].leaveRGB));
                }
                else
                {
                    players.Add(userid, new OAPlayer(false, announcement, useraccountname, userid));
                    db.Query("INSERT INTO onlineannounce (userid, leaving, greetrgb, leavergb) VALUES (@0, @1, @2, @3);", userid.ToString(), announcement, colorToString(players[userid].greetRGB), colorToString(players[userid].leaveRGB));
                }
            }
        }

        private void removeAnnounce(bool isGreeting, int userid)
        {
            if (isGreeting)
            {
                players[userid].greet = null;
                db.Query("UPDATE onlineannounce SET greet=null WHERE userid=@0;", userid.ToString());
            }
            else
            {
                players[userid].leave = null;
                db.Query("UPDATE onlineannounce SET leaving=null WHERE userid=@0;", userid.ToString());
            }
        }

        private void setColor(bool isGreeting, int userid, Color color)
        {
            if (isGreeting)
            {
                players[userid].greetRGB = color;
                db.Query("UPDATE onlineannounce SET greetrgb=@0 WHERE userid=@1;", colorToString(color), userid);
            }
            else
            {
                players[userid].leaveRGB = color;
                db.Query("UPDATE onlineannounce SET leavergb=@0 WHERE userid=@1;", colorToString(color), userid);
            }
        }

        #endregion
    }
}