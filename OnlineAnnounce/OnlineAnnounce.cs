using Mono.Data.Sqlite;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;

namespace OnlineAnnounce
{
    [ApiVersion(1, 16)]
    public class OnlineAnnounce : TerrariaPlugin
    {
        public override string Name { get { return "OnlineAnnounce"; } }
        public override string Author { get { return "Zaicon"; } }
        public override string Description { get { return "Broadcasts an custom announcement upon player join."; } }
        public override Version Version { get { return new Version(1, 3, 3, 3); } }

        private static IDbConnection db;
        private static Config config = new Config();
        public string configPath = Path.Combine(TShock.SavePath, "OnlineAnnounceConfig.json");
        private List<string> badwords = new List<string>();
        private List<int> greeted = new List<int>();

        public OnlineAnnounce(Main game)
            : base(game)
        {
            base.Order = -1;
        }

        #region Initialize/Dispose
        public override void Initialize()
        {
            ServerApi.Hooks.GameInitialize.Register(this, OnInitialize);
            ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreet);
            ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
            PlayerHooks.PlayerPostLogin += OnPostLogin;
        }

        protected override void Dispose(bool Disposing)
        {
            if (Disposing)
            {
                ServerApi.Hooks.GameInitialize.Deregister(this, OnInitialize);
                ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreet);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
                PlayerHooks.PlayerPostLogin -= OnPostLogin;
            }
            base.Dispose(Disposing);
        }
        #endregion

        #region Hooks
        private void OnInitialize(EventArgs args)
        {
            DBConnect(); //Connect to (or create) the database table
            loadConfig();

            Commands.ChatCommands.Add(new Command("greet.use", UGreet, "greet"));
            Commands.ChatCommands.Add(new Command("greet.reload", UReload, "greetreload"));
        }

        private void OnGreet(GreetPlayerEventArgs args)
        {
            if (!greeted.Contains(TShock.Players[args.Who].Index) && hasGreet(TShock.Players[args.Who].UserID))
            {
                TSPlayer.All.SendMessage("[" + TShock.Players[args.Who].UserAccountName + "] " + getGreet(TShock.Players[args.Who].UserID), getRGB(TShock.Players[args.Who].UserID));
                TShock.Players[args.Who].SendMessage("[" + TShock.Players[args.Who].UserAccountName + "] " + getGreet(TShock.Players[args.Who].UserID), getRGB(TShock.Players[args.Who].UserID));
                greeted.Add(TShock.Players[args.Who].Index);
            }
        }

        private void OnPostLogin(PlayerPostLoginEventArgs args)
        {
            if (!greeted.Contains(args.Player.Index) && hasGreet(args.Player.UserID))
            {
                TSPlayer.All.SendMessage("[" + args.Player.UserAccountName + "] " + getGreet(args.Player.UserID), getRGB(args.Player.UserID));
                greeted.Add(args.Player.Index);
            }
        }

        private void OnLeave(LeaveEventArgs args)
        {
            if (greeted.Contains(args.Who))
                greeted.Remove(args.Who);
        }
        #endregion

        #region Greet commands
        private void UGreet(CommandArgs args)
        {
            if (args.Parameters.Count == 0)
            {
                args.Player.SendErrorMessage("Invalid syntax:");
                args.Player.SendErrorMessage("/greet set <greeting>");
                if (!args.Player.Group.HasPermission("greet.mod"))
                {
                    args.Player.SendErrorMessage("/greet read");
                    args.Player.SendErrorMessage("/greet remove");
                }
                else
                {
                    args.Player.SendErrorMessage("/greet setcolor <player> <r> <g> <b>");
                    args.Player.SendErrorMessage("/greet read");
                    args.Player.SendErrorMessage("/greet remove [player]");
                    args.Player.SendErrorMessage("/greet setother <player> <greeting>");
                    args.Player.SendErrorMessage("/greet readother <player>");
                    args.Player.SendErrorMessage("/greet lock <player>");
                }
            }
            else if (args.Parameters.Count == 1)
            {
                if (args.Parameters[0].ToLower() == "read")
                {
                    if (hasGreet(args.Player.UserID))
                    {
                        args.Player.SendInfoMessage("Your greeting: ");
                        args.Player.SendMessage("[" + args.Player.UserAccountName + "] " + getGreet(args.Player.UserID), getRGB(args.Player.UserID));
                    }
                    else
                        args.Player.SendInfoMessage("You do not have a greeting set. Use /greet set <greeting> to set a greeting.");
                }
                else if (args.Parameters[0].ToLower() == "remove")
                {
                    if (hasGreet(args.Player.UserID))
                    {
                        if (isLocked(args.Player.UserID) && (!args.Player.Group.HasPermission("greet.mod") || !args.Player.Group.HasPermission("greet.admin")))
                            args.Player.SendErrorMessage("You do not have permission to remove your greeting!");
                        else
                        {
                            removeGreet(args.Player.UserID);
                            args.Player.SendSuccessMessage("Your greeting was removed successfully.");
                        }
                    }
                    else
                        args.Player.SendErrorMessage("You do not have a greeting to remove.");
                }
                else
                {
                    args.Player.SendErrorMessage("Invalid syntax:");
                    args.Player.SendErrorMessage("/greet set <greeting>");
                    args.Player.SendErrorMessage("/greet read");
                    if (!args.Player.Group.HasPermission("greet.mod"))
                        args.Player.SendErrorMessage("/greet remove");
                    else
                    {
                        args.Player.SendErrorMessage("/greet remove [player]");
                        args.Player.SendErrorMessage("/greet setother <player> <greeting>");
                        args.Player.SendErrorMessage("/greet readother <player>");
                        args.Player.SendErrorMessage("/greet lock <player>");
                    }
                }
            }
            else if (args.Parameters.Count == 2)
            {
                if (args.Parameters[0].ToLower() == "set")
                {
                    if (isLocked(args.Player.UserID) && (!args.Player.Group.HasPermission("greet.mod") || !args.Player.Group.HasPermission("greet.admin")))
                        args.Player.SendErrorMessage("You do not have permission to change your greeting!");
                    else
                    {
                        if (setGreet(args.Player.UserID, args.Parameters[1], args.Player.Group.HasPermission("greet.admin") ? true : false))
                        {
                            args.Player.SendSuccessMessage("Your greeting has been set to: ");
                            args.Player.SendMessage("[" + args.Player.UserAccountName + "] " + getGreet(args.Player.UserID), getRGB(args.Player.UserID));
                        }
                        else
                            args.Player.SendErrorMessage("Your greeting contained a forbidden word and may not be used as a greeting.");
                    }
                }
                else if (args.Parameters[0].ToLower() == "remove")
                {
                    if (!args.Player.Group.HasPermission("greet.mod") || !args.Player.Group.HasPermission("greet.admin"))
                    {
                        args.Player.SendErrorMessage("You do not have permission to remove other greetings.");
                        return;
                    }

                    User plr = TShock.Users.GetUserByName(args.Parameters[1]);
                    if (plr == null)
                        args.Player.SendErrorMessage("Invalid player.");
                    else if (!hasGreet(plr.ID))
                        args.Player.SendErrorMessage("This player does not have a greeting to remove.");
                    else
                    {
                        removeGreet(plr.ID);
                        args.Player.SendSuccessMessage("{0}'s greeting was removed successfully.", plr.Name);
                    }
                }
                else if (args.Parameters[0].ToLower() == "readother")
                {
                    if (!args.Player.Group.HasPermission("greet.mod") || !args.Player.Group.HasPermission("greet.admin"))
                    {
                        args.Player.SendErrorMessage("You do not have permission to read other greetings.");
                        return;
                    }

                    User plr = TShock.Users.GetUserByName(args.Parameters[1]);
                    if (plr == null)
                        args.Player.SendErrorMessage("Invalid player.");
                    else
                        args.Player.SendMessage("[" + plr.Name + "] " + getGreet(plr.ID), getRGB(plr.ID));
                }
                else if (args.Parameters[0].ToLower() == "lock")
                {
                    if (!args.Player.Group.HasPermission("greet.mod") || !args.Player.Group.HasPermission("greet.admin"))
                    {
                        args.Player.SendErrorMessage("You do not have permission to lock other greetings.");
                        return;
                    }

                    List<TSPlayer> plrs = TShock.Utils.FindPlayer(args.Parameters[1]);
                    if (plrs.Count == 0)
                        args.Player.SendErrorMessage("No players found");
                    else if (plrs.Count > 1)
                    {
                        string outputplrs = string.Join(", ", plrs.Select(p => p.UserAccountName));
                        args.Player.SendErrorMessage("Multiple players found: " + outputplrs);
                    }
                    else if (!hasGreet(plrs[0].UserID))
                        args.Player.SendErrorMessage("This player does not have a greeting to lock.");
                    else
                    {
                        bool waslocked = lockGreet(plrs[0].UserID);
                        args.Player.SendSuccessMessage("{0}'s greeting was {1}locked successfully.", plrs[0].UserAccountName, (waslocked ? "" : "un"));
                    }
                }
                else
                {
                    args.Player.SendErrorMessage("Invalid syntax:");
                    args.Player.SendErrorMessage("/greet set <greeting>");
                    args.Player.SendErrorMessage("/greet read");
                    if (!args.Player.Group.HasPermission("greet.mod"))
                        args.Player.SendErrorMessage("/greet remove");
                    else
                    {
                        args.Player.SendErrorMessage("/greet remove [player]");
                        args.Player.SendErrorMessage("/greet setother <player> <greeting>");
                        args.Player.SendErrorMessage("/greet readother <player>");
                        args.Player.SendErrorMessage("/greet lock <player>");
                    }
                }
            }
            else //if (args.Parameters.Count > 2)
            {
                if (args.Parameters[0].ToLower() == "set")
                {
                    if (isLocked(args.Player.UserID) && (!args.Player.Group.HasPermission("greet.mod") || !args.Player.Group.HasPermission("greet.admin")))
                        args.Player.SendErrorMessage("You do not have permission to change your greeting!");
                    else
                    {
                        string greet = string.Join(" ", args.Parameters);
                        greet = greet.Replace("set ", "");

                        if (setGreet(args.Player.UserID, greet, args.Player.Group.HasPermission("greet.admin") ? true : false))
                        {
                            args.Player.SendSuccessMessage("Your greeting has been set to:");
                            args.Player.SendMessage("[" + args.Player.UserAccountName + "] " + getGreet(args.Player.UserID), getRGB(args.Player.UserID));
                        }
                        else
                            args.Player.SendErrorMessage("Your greeting contained a forbidden word and may not be used as a greeting.");
                    }
                }
                else if (args.Parameters[0].ToLower() == "setcolor")
                {
                    if (!args.Player.Group.HasPermission("greet.mod") && !args.Player.Group.HasPermission("greet.admin"))
                    {
                        args.Player.SendErrorMessage("You do not have permission to set greeting colors!");
                        return;
                    }

                    if (args.Parameters.Count != 5)
                        args.Player.SendErrorMessage("Invalid syntax: /greet setcolor <player> <r> <g> <b>");
                    else
                    {
                        string plr = args.Parameters[1];
                        List<TSPlayer> listofplayers = TShock.Utils.FindPlayer(plr);
                        if (listofplayers.Count < 1)
                            args.Player.SendErrorMessage("Invalid player!");
                        else if (listofplayers.Count > 1)
                        {
                            string outputlistofplayers = string.Join(", ", listofplayers.Select(p => p.UserAccountName));
                            args.Player.SendErrorMessage("Multiple players found: " + outputlistofplayers);
                        }
                        else if (!hasGreet(listofplayers[0].UserID))
                        {
                            args.Player.SendErrorMessage("This player does not have a greeting to change the color of!");
                        }
                        else if ((listofplayers[0].Group.HasPermission("greet.mod") && !args.Player.Group.HasPermission("greet.admin")) && listofplayers[0] != args.Player)
                            args.Player.SendErrorMessage("You do not have permission to change this player's greeting color!");
                        else
                        {
                            int[] rgbcolor = { 127, 255, 212 };
                            bool[] isParsed = { false, false, false };

                            isParsed[0] = int.TryParse(args.Parameters[2], out rgbcolor[0]);
                            isParsed[1] = int.TryParse(args.Parameters[3], out rgbcolor[1]);
                            isParsed[2] = int.TryParse(args.Parameters[4], out rgbcolor[2]);

                            if (isParsed.Contains(false))
                            {
                                args.Player.SendErrorMessage("Invalid RGB colors!");
                                return;
                            }

                            setRGB(listofplayers[0].UserID, rgbcolor);
                            args.Player.SendSuccessMessage("Greeting color set successfully.");
                        }
                    }
                }
                else if (args.Parameters[0].ToLower() == "setother")
                {
                    if (!args.Player.Group.HasPermission("greet.mod") || !args.Player.Group.HasPermission("greet.admin"))
                    {
                        args.Player.SendErrorMessage("You do not have permission to set other greetings.");
                        return;
                    }

                    string plr = args.Parameters[1];
                    List<TSPlayer> listofplayers = TShock.Utils.FindPlayer(plr);
                    if (listofplayers.Count < 1)
                        args.Player.SendErrorMessage("Invalid player!");
                    else if (listofplayers.Count > 1)
                    {
                        string outputlistofplayers = string.Join(", ", listofplayers.Select(p => p.UserAccountName));
                        args.Player.SendErrorMessage("Multiple players found: " + outputlistofplayers);
                    }
                    else if (listofplayers[0].Group.HasPermission("greet.mod") && !args.Player.Group.HasPermission("greet.admin"))
                    {
                        args.Player.SendErrorMessage("You cannot edit this person's greeting!");
                        return;
                    }
                    else
                    {
                        string greet = string.Join(" ", args.Parameters);
                        string replace = "setother " + args.Parameters[1] + " ";
                        greet = greet.Replace(replace, "");

                        if (setGreet(listofplayers[0].UserID, greet, args.Player.Group.HasPermission("greet.admin") ? true : false))
                        {
                            args.Player.SendSuccessMessage("{0}'s greeting was set to:", listofplayers[0].Name);
                            args.Player.SendMessage("[" + listofplayers[0].UserAccountName + "] " + getGreet(listofplayers[0].UserID), getRGB(listofplayers[0].UserID));
                        }
                        else
                            args.Player.SendErrorMessage("Your greeting contained a forbidden word and cannot be used as a greeting.");
                    }
                }
                else
                {
                    args.Player.SendErrorMessage("Invalid syntax:");
                    args.Player.SendErrorMessage("/greet set <greeting>");
                    args.Player.SendErrorMessage("/greet read");
                    if (!args.Player.Group.HasPermission("greet.mod"))
                        args.Player.SendErrorMessage("/greet remove");
                    else
                    {
                        args.Player.SendErrorMessage("/greet remove [player]");
                        args.Player.SendErrorMessage("/greet setother <player> <greeting>");
                        args.Player.SendErrorMessage("/greet readother <player>");
                        args.Player.SendErrorMessage("/greet lock <player>");
                    }
                }
            }
        }

        private void UReload(CommandArgs args)
        {
            loadConfig();
            args.Player.SendSuccessMessage("Greet config reloaded.");
        }
        #endregion

        #region Database commands
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
                    string sql = Path.Combine(TShock.SavePath, "Greetings.sqlite");
                    db = new SqliteConnection(string.Format("uri=file://{0},Version=3", sql));
                    break;

            }

            SqlTableCreator sqlcreator = new SqlTableCreator(db, db.GetSqlType() == SqlType.Sqlite ? (IQueryBuilder)new SqliteQueryCreator() : new MysqlQueryCreator());

            sqlcreator.EnsureExists(new SqlTable("Greetings",
                new SqlColumn("UserID", MySqlDbType.Int32) { Primary = true, Unique = true, Length = 4 },
                new SqlColumn("Greeting", MySqlDbType.Text) { Length = 50 },
                new SqlColumn("Locked", MySqlDbType.Int32) { Length = 1 },
                new SqlColumn("R", MySqlDbType.Int32) { Length = 3 },
                new SqlColumn("G", MySqlDbType.Int32) { Length = 3 },
                new SqlColumn("B", MySqlDbType.Int32) { Length = 3 }));
        }

        private string getGreet(int userid)
        {
            if (hasGreet(userid))
            {
                using (QueryResult reader = db.QueryReader(@"SELECT * FROM Greetings WHERE UserID=@0", userid))
                {
                    if (reader.Read())
                        return reader.Get<string>("Greeting");
                    else
                        return null; //This should never happen
                }
            }
            else
                return null; //This should never happen
        }

        private Color getRGB(int userid)
        {
            byte[] uacolor = { (byte)127, (byte)255, (byte)212 };

            if (hasGreet(userid))
            {
                using (QueryResult reader = db.QueryReader(@"SELECT * FROM Greetings WHERE UserID=@0", userid))
                {
                    if (reader.Read())
                    {
                        uacolor[0] = (byte)reader.Get<int>("R");
                        uacolor[1] = (byte)reader.Get<int>("G");
                        uacolor[2] = (byte)reader.Get<int>("B");
                    }
                }

                return new Color(uacolor[0], uacolor[1], uacolor[2]);
            }
            else
                return new Color(uacolor[0], uacolor[1], uacolor[2]); //This should never happen
        }

        private bool hasGreet(int userid)
        {
            using (QueryResult reader = db.QueryReader(@"SELECT * FROM Greetings WHERE UserID=@0", userid))
            {
                if (reader.Read())
                    return true;
                else
                    return false;
            }
        }

        private bool setGreet(int userid, string greet, bool ignore)
        {
            if (!ignore)
                foreach (string badword in badwords)
                    if (greet.ToLower().Contains(badword.ToLower()))
                        return false;

            if (hasGreet(userid))
                db.Query("UPDATE Greetings SET Greeting=@0 WHERE UserID=@1", greet, userid);
            else
                db.Query("INSERT INTO Greetings (UserID, Greeting, Locked, R, G, B) VALUES (@0, @1, 0, 127, 255, 212)", userid, greet);

            return true;
        }

        private void setRGB(int userid, int[] rgbcolor)
        {
            if (hasGreet(userid))
            {
                db.Query("UPDATE Greetings SET R=@0 WHERE UserID=@1", rgbcolor[0], userid);
                db.Query("UPDATE Greetings SET G=@0 WHERE UserID=@1", rgbcolor[1], userid);
                db.Query("UPDATE Greetings SET B=@0 WHERE UserID=@1", rgbcolor[2], userid);
            }
        }

        private void removeGreet(int userid)
        {
            if (hasGreet(userid))
                db.Query("DELETE FROM Greetings WHERE UserID=@0", userid);
        }

        private bool lockGreet(int userid)
        {
            using (QueryResult reader = db.QueryReader(@"SELECT * FROM Greetings WHERE UserID=@0", userid))
            {
                if (reader.Read())
                {
                    int islocked = reader.Get<int>("Locked");
                    if (islocked == 1)
                    {
                        db.Query("UPDATE Greetings SET Locked=0 WHERE UserID=@0", userid);
                        return false;
                    }
                    else
                    {
                        db.Query("UPDATE Greetings SET Locked=1 WHERE UserID=@0", userid);
                        return true;
                    }
                }
                else
                    return false; //This should never happen
            }
        }

        private bool isLocked(int userid)
        {
            using (QueryResult reader = db.QueryReader(@"SELECT * FROM Greetings WHERE UserID=@0", userid))
            {
                if (reader.Read())
                {
                    int islocked = reader.Get<int>("Locked");
                    if (islocked == 1)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                    return false; //This should never happen
            }
        }
        #endregion

        private void loadConfig()
        {
            (config = Config.Read(configPath)).Write(configPath);

            try
            {
                badwords.Clear();
                foreach (string badword in config.Badwords)
                    badwords.Add(badword);
            }
            catch
            {
                Console.WriteLine("Error reading OnlineAnnounceConfig.json: Invalid badwords format.");
                Log.Error("Error reading OnlineAnnounceConfig.json: Invalid badwords format.");
            }
        }
    }
}
