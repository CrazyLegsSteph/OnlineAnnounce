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
    public class Joiners
    {
        public TSPlayer player;
        public Timer announce;
        public bool hasgreeted;

        public Joiners(TSPlayer tsp)
        {
            player = tsp;
            announce = new Timer() { AutoReset = false, Enabled = false, Interval = 1000 };
            announce.Elapsed += announce_Elapsed;
            hasgreeted = false;

            if (tsp.IsLoggedIn && OnlineAnnounce.hasGreet(player.UserID) && !hasgreeted)
            {
                announce.Enabled = true;
                hasgreeted = true;
            }
        }

        void announce_Elapsed(object sender, ElapsedEventArgs e)
        {
                TSPlayer.All.SendMessage("[" + player.UserAccountName + "] " + OnlineAnnounce.getGreet(player.UserID), OnlineAnnounce.getGreetRGB(player.UserID));
        }
    }

    [ApiVersion(1, 16)]
    public class OnlineAnnounce : TerrariaPlugin
    {
        public override string Name { get { return "OnlineAnnounce"; } }
        public override string Author { get { return "Zaicon"; } }
        public override string Description { get { return "Broadcasts an custom announcement upon player join/leave."; } }
        public override Version Version { get { return new Version(3, 3, 1, 1); } }

        private static IDbConnection db;
        private static Config config = new Config();
        public string configPath = Path.Combine(TShock.SavePath, "OnlineAnnounceConfig.json");
        private List<string> badwords = new List<string>();
        private List<Joiners> joined = new List<Joiners>();

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
            PlayerHooks.PlayerPostLogin += OnPostLogin;
        }

        protected override void Dispose(bool Disposing)
        {
            if (Disposing)
            {
                ServerApi.Hooks.GameInitialize.Deregister(this, OnInitialize);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
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

            Commands.ChatCommands.Add(new Command("greet.greet", UGreet, "greet"));
            Commands.ChatCommands.Add(new Command("greet.leave", ULeave, "leave"));
            Commands.ChatCommands.Add(new Command("greet.reload", UReload, "greetreload"));
        }

        private void OnPostLogin(PlayerPostLoginEventArgs args)
        {
            if (TShock.Players[args.Player.Index] == null)
                return;

            bool c = false;

            foreach (Joiners j in joined)
                if (j.player.UserID == args.Player.UserID)
                {
                    c = true;
                    if (hasGreet(j.player.UserID) && !j.hasgreeted)
                    {
                        j.announce.Enabled = true;
                        j.hasgreeted = true;
                        return;
                    }
                }

            if (!c)
            {
                joined.Add(new Joiners(args.Player));
            }
        }

        private void OnLeave(LeaveEventArgs args)
        {
            for (int i = 0; i < joined.Count; i++)
                if (joined[i].player.Index == args.Who)
                {
                    joined.RemoveAt(i);
                    break;
                }

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
                }
            }
            else if (args.Parameters.Count == 1)
            {
                if (args.Parameters[0].ToLower() == "read")
                {
                    if (hasGreet(args.Player.UserID))
                    {
                        args.Player.SendInfoMessage("Your greeting: ");
                        args.Player.SendMessage("[" + args.Player.UserAccountName + "] " + getGreet(args.Player.UserID), getGreetRGB(args.Player.UserID));
                    }
                    else
                        args.Player.SendInfoMessage("You do not have a greeting set. Use /greet set <greeting> to set a greeting.");
                }
                else if (args.Parameters[0].ToLower() == "remove")
                {
                    if (hasGreet(args.Player.UserID))
                    {
                        removeGreet(args.Player.UserID);
                        args.Player.SendSuccessMessage("Your greeting was removed successfully.");
                    }
                    else
                        args.Player.SendErrorMessage("You do not have a greeting to remove.");
                }
                else
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
                    }
                }
            }
            else if (args.Parameters.Count == 2)
            {
                if (args.Parameters[0].ToLower() == "set")
                {
                    if (!args.Player.RealPlayer)
                    {
                        args.Player.SendErrorMessage("You may not set your greeting from here.");
                        return;
                    }

                    if (setGreet(args.Player.UserID, args.Parameters[1], args.Player.Group.HasPermission("greet.admin") ? true : false))
                    {
                        args.Player.SendSuccessMessage("Your greeting has been set to: ");
                        args.Player.SendMessage("[" + args.Player.UserAccountName + "] " + getGreet(args.Player.UserID), getGreetRGB(args.Player.UserID));
                    }
                    else
                        args.Player.SendErrorMessage("Your greeting contained a forbidden word and may not be used as a greeting.");
                }
                else if (args.Parameters[0].ToLower() == "remove")
                {
                    if (!args.Player.Group.HasPermission("greet.mod") && !args.Player.Group.HasPermission("greet.admin"))
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
                    if (!args.Player.Group.HasPermission("greet.mod") && !args.Player.Group.HasPermission("greet.admin"))
                    {
                        args.Player.SendErrorMessage("You do not have permission to read other greetings.");
                        return;
                    }

                    User plr = TShock.Users.GetUserByName(args.Parameters[1]);
                    if (plr == null)
                        args.Player.SendErrorMessage("Invalid player.");
                    else
                        args.Player.SendMessage("[" + plr.Name + "] " + getGreet(plr.ID), getGreetRGB(plr.ID));
                }
                else
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
                    }
                }
            }
            else //if (args.Parameters.Count > 2)
            {
                if (args.Parameters[0].ToLower() == "set")
                {
                    if (!args.Player.RealPlayer)
                    {
                        args.Player.SendErrorMessage("You may not set your greeting from here.");
                        return;
                    }

                    string greet = string.Join(" ", args.Parameters);
                    greet = greet.Replace("set ", "");

                    if (setGreet(args.Player.UserID, greet, args.Player.Group.HasPermission("greet.admin") ? true : false))
                    {
                        args.Player.SendSuccessMessage("Your greeting has been set to:");
                        args.Player.SendMessage("[" + args.Player.UserAccountName + "] " + getGreet(args.Player.UserID), getGreetRGB(args.Player.UserID));
                    }
                    else
                        args.Player.SendErrorMessage("Your greeting contained a forbidden word and may not be used as a greeting.");
                }
                else if (args.Parameters[0].ToLower() == "setcolor")
                {
                    if (!args.Player.RealPlayer)
                    {
                        args.Player.SendErrorMessage("You may not set your greeting color from here.");
                        return;
                    }

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
                            TShock.Utils.SendMultipleMatchError(args.Player, listofplayers.Select(p => p.UserAccountName));
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

                            setGreetRGB(listofplayers[0].UserID, rgbcolor);
                            args.Player.SendSuccessMessage("Greeting color set successfully.");
                        }
                    }
                }
                else if (args.Parameters[0].ToLower() == "setother")
                {
                    if (!args.Player.RealPlayer)
                    {
                        args.Player.SendErrorMessage("You may not set a greeting from here.");
                        return;
                    }

                    if (!args.Player.Group.HasPermission("greet.mod") && !args.Player.Group.HasPermission("greet.admin"))
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
                        TShock.Utils.SendMultipleMatchError(args.Player, listofplayers.Select(p => p.UserAccountName));
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
                            args.Player.SendMessage("[" + listofplayers[0].UserAccountName + "] " + getGreet(listofplayers[0].UserID), getGreetRGB(listofplayers[0].UserID));
                        }
                        else
                            args.Player.SendErrorMessage("Your greeting contained a forbidden word and cannot be used as a greeting.");
                    }
                }
                else
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
                    }
                }
            }
        }
        #endregion

        #region Leave command
        private void ULeave(CommandArgs args)
        {
            if (args.Parameters.Count == 0)
            {
                args.Player.SendErrorMessage("Invalid syntax:");
                args.Player.SendErrorMessage("/leave set <leave message>");
                if (!args.Player.Group.HasPermission("leave.mod"))
                {
                    args.Player.SendErrorMessage("/leave read");
                    args.Player.SendErrorMessage("/leave remove");
                }
                else
                {
                    args.Player.SendErrorMessage("/leave setcolor <player> <r> <g> <b>");
                    args.Player.SendErrorMessage("/leave read");
                    args.Player.SendErrorMessage("/leave remove [player]");
                    args.Player.SendErrorMessage("/leave setother <player> <leave message>");
                    args.Player.SendErrorMessage("/leave readother <player>");
                }
            }
            else if (args.Parameters.Count == 1)
            {
                if (args.Parameters[0].ToLower() == "read")
                {
                    if (hasLeave(args.Player.UserID))
                    {
                        args.Player.SendInfoMessage("Your leaving message: ");
                        args.Player.SendMessage(getLeave(args.Player.UserID), getLeaveRGB(args.Player.UserID));
                    }
                    else
                        args.Player.SendInfoMessage("You do not have a leaving message set. Use /leave set <leaving message> to set a leaving message.");
                }
                else if (args.Parameters[0].ToLower() == "remove")
                {
                    if (hasLeave(args.Player.UserID))
                    {
                        removeLeave(args.Player.UserID);
                        args.Player.SendSuccessMessage("Your leaving message was removed successfully.");
                    }
                    else
                        args.Player.SendErrorMessage("You do not have a leaving message to remove.");
                }
                else
                {
                    args.Player.SendErrorMessage("Invalid syntax:");
                    args.Player.SendErrorMessage("/leave set <leaving message>");
                    if (!args.Player.Group.HasPermission("leave.mod"))
                    {
                        args.Player.SendErrorMessage("/leave read");
                        args.Player.SendErrorMessage("/leave remove");
                    }
                    else
                    {
                        args.Player.SendErrorMessage("/leave setcolor <player> <r> <g> <b>");
                        args.Player.SendErrorMessage("/leave read");
                        args.Player.SendErrorMessage("/leave remove [player]");
                        args.Player.SendErrorMessage("/leave setother <player> <leaving message>");
                        args.Player.SendErrorMessage("/leave readother <player>");
                    }
                }
            }
            else if (args.Parameters.Count == 2)
            {
                if (args.Parameters[0].ToLower() == "set")
                {
                    if (!args.Player.RealPlayer)
                    {
                        args.Player.SendErrorMessage("You may not set your leaving message from here.");
                        return;
                    }

                    if (setLeave(args.Player.UserID, args.Parameters[1], args.Player.Group.HasPermission("leave.admin") ? true : false))
                    {
                        args.Player.SendSuccessMessage("Your leaving message has been set to: ");
                        args.Player.SendMessage(getLeave(args.Player.UserID), getLeaveRGB(args.Player.UserID));
                    }
                    else
                        args.Player.SendErrorMessage("Your leaving message contained a forbidden word and may not be used as a leaving message.");
                }
                else if (args.Parameters[0].ToLower() == "remove")
                {
                    if (!args.Player.Group.HasPermission("leave.mod") && !args.Player.Group.HasPermission("leave.admin"))
                    {
                        args.Player.SendErrorMessage("You do not have permission to remove other leaving messages.");
                        return;
                    }

                    User plr = TShock.Users.GetUserByName(args.Parameters[1]);
                    if (plr == null)
                        args.Player.SendErrorMessage("Invalid player.");
                    else if (!hasLeave(plr.ID))
                        args.Player.SendErrorMessage("This player does not have a leaving message to remove.");
                    else
                    {
                        removeLeave(plr.ID);
                        args.Player.SendSuccessMessage("{0}'s leaving message was removed successfully.", plr.Name);
                    }
                }
                else if (args.Parameters[0].ToLower() == "readother")
                {
                    if (!args.Player.Group.HasPermission("leave.mod") && !args.Player.Group.HasPermission("leave.admin"))
                    {
                        args.Player.SendErrorMessage("You do not have permission to read other leaving message.");
                        return;
                    }

                    User plr = TShock.Users.GetUserByName(args.Parameters[1]);
                    if (plr == null)
                        args.Player.SendErrorMessage("Invalid player.");
                    else
                        args.Player.SendMessage(getLeave(plr.ID), getLeaveRGB(plr.ID));
                }
                else
                {
                    args.Player.SendErrorMessage("Invalid syntax:");
                    args.Player.SendErrorMessage("/leave set <leaving message>");
                    if (!args.Player.Group.HasPermission("leave.mod"))
                    {
                        args.Player.SendErrorMessage("/leave read");
                        args.Player.SendErrorMessage("/leave remove");
                    }
                    else
                    {
                        args.Player.SendErrorMessage("/leave setcolor <player> <r> <g> <b>");
                        args.Player.SendErrorMessage("/leave read");
                        args.Player.SendErrorMessage("/leave remove [player]");
                        args.Player.SendErrorMessage("/leave setother <player> <leaving message>");
                        args.Player.SendErrorMessage("/leave readother <player>");
                    }
                }
            }
            else //if (args.Parameters.Count > 2)
            {
                if (args.Parameters[0].ToLower() == "set")
                {
                    if (!args.Player.RealPlayer)
                    {
                        args.Player.SendErrorMessage("You may not set your leaving message from here.");
                        return;
                    }

                    string leave = string.Join(" ", args.Parameters);
                    leave = leave.Replace("set ", "");

                    if (setLeave(args.Player.UserID, leave, args.Player.Group.HasPermission("leave.admin") ? true : false))
                    {
                        args.Player.SendSuccessMessage("Your leaving message has been set to:");
                        args.Player.SendMessage(getLeave(args.Player.UserID), getLeaveRGB(args.Player.UserID));
                    }
                    else
                        args.Player.SendErrorMessage("Your leaving message contained a forbidden word and may not be used as a leaving message.");
                }
                else if (args.Parameters[0].ToLower() == "setcolor")
                {
                    if (!args.Player.RealPlayer)
                    {
                        args.Player.SendErrorMessage("You may not set your leaving message color from here.");
                        return;
                    }

                    if (!args.Player.Group.HasPermission("leave.mod") && !args.Player.Group.HasPermission("leave.admin"))
                    {
                        args.Player.SendErrorMessage("You do not have permission to set leaving message colors!");
                        return;
                    }

                    if (args.Parameters.Count != 5)
                        args.Player.SendErrorMessage("Invalid syntax: /leave setcolor <player> <r> <g> <b>");
                    else
                    {
                        string plr = args.Parameters[1];
                        List<TSPlayer> listofplayers = TShock.Utils.FindPlayer(plr);
                        if (listofplayers.Count < 1)
                            args.Player.SendErrorMessage("Invalid player!");
                        else if (listofplayers.Count > 1)
                        {
                            TShock.Utils.SendMultipleMatchError(args.Player, listofplayers.Select(p => p.UserAccountName));
                        }
                        else if (!hasLeave(listofplayers[0].UserID))
                        {
                            args.Player.SendErrorMessage("This player does not have a leaving message to change the color of!");
                        }
                        else if ((listofplayers[0].Group.HasPermission("leave.mod") && !args.Player.Group.HasPermission("leave.admin")) && listofplayers[0] != args.Player)
                            args.Player.SendErrorMessage("You do not have permission to change this player's leaving message color!");
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

                            setLeaveRGB(listofplayers[0].UserID, rgbcolor);
                            args.Player.SendSuccessMessage("Leaving message color set successfully.");
                        }
                    }
                }
                else if (args.Parameters[0].ToLower() == "setother")
                {
                    if (!args.Player.RealPlayer)
                    {
                        args.Player.SendErrorMessage("You may not set a leaving message from here.");
                        return;
                    }

                    if (!args.Player.Group.HasPermission("leave.mod") && !args.Player.Group.HasPermission("leave.admin"))
                    {
                        args.Player.SendErrorMessage("You do not have permission to set other leaving message.");
                        return;
                    }

                    string plr = args.Parameters[1];
                    List<TSPlayer> listofplayers = TShock.Utils.FindPlayer(plr);
                    if (listofplayers.Count < 1)
                        args.Player.SendErrorMessage("Invalid player!");
                    else if (listofplayers.Count > 1)
                    {
                        TShock.Utils.SendMultipleMatchError(args.Player, listofplayers.Select(p => p.UserAccountName));
                    }
                    else if (listofplayers[0].Group.HasPermission("leave.mod") && !args.Player.Group.HasPermission("leave.admin"))
                    {
                        args.Player.SendErrorMessage("You cannot edit this person's leaving message!");
                        return;
                    }
                    else
                    {
                        string leave = string.Join(" ", args.Parameters);
                        string replace = "setother " + args.Parameters[1] + " ";
                        leave = leave.Replace(replace, "");

                        if (setLeave(listofplayers[0].UserID, leave, args.Player.Group.HasPermission("leave.admin") ? true : false))
                        {
                            args.Player.SendSuccessMessage("{0}'s leaving message was set to:", listofplayers[0].Name);
                            args.Player.SendMessage(getLeave(listofplayers[0].UserID), getLeaveRGB(listofplayers[0].UserID));
                        }
                        else
                            args.Player.SendErrorMessage("Your leaving message contained a forbidden word and cannot be used as a leaving message.");
                    }
                }
                else
                {
                    args.Player.SendErrorMessage("Invalid syntax:");
                    args.Player.SendErrorMessage("/leave set <leaving message>");
                    if (!args.Player.Group.HasPermission("leave.mod"))
                    {
                        args.Player.SendErrorMessage("/leave read");
                        args.Player.SendErrorMessage("/leave remove");
                    }
                    else
                    {
                        args.Player.SendErrorMessage("/leave setcolor <player> <r> <g> <b>");
                        args.Player.SendErrorMessage("/leave read");
                        args.Player.SendErrorMessage("/leave remove [player]");
                        args.Player.SendErrorMessage("/leave setother <player> <leaving message>");
                        args.Player.SendErrorMessage("/leave readother <player>");
                    }
                }
            }
        }
        #endregion


        private void UReload(CommandArgs args)
        {
            loadConfig();
            args.Player.SendSuccessMessage("Greet config reloaded.");
        }

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

            sqlcreator.EnsureTableStructure(new SqlTable("Greetings",
                new SqlColumn("UserID", MySqlDbType.Int32) { Primary = true, Unique = true, Length = 4 },
                new SqlColumn("Greeting", MySqlDbType.Text) { Length = 50 },
                new SqlColumn("R", MySqlDbType.Int32) { Length = 3 },
                new SqlColumn("G", MySqlDbType.Int32) { Length = 3 },
                new SqlColumn("B", MySqlDbType.Int32) { Length = 3 }));

            sqlcreator.EnsureTableStructure(new SqlTable("Leavings",
                new SqlColumn("UserID", MySqlDbType.Int32) { Primary = true, Unique = true, Length = 4 },
                new SqlColumn("Leaving", MySqlDbType.Text) { Length = 50 },
                new SqlColumn("R", MySqlDbType.Int32) { Length = 3 },
                new SqlColumn("G", MySqlDbType.Int32) { Length = 3 },
                new SqlColumn("B", MySqlDbType.Int32) { Length = 3 }));
        }

        #region GreetCommands
        public static string getGreet(int userid)
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

        public static Color getGreetRGB(int userid)
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

        public static bool hasGreet(int userid)
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
                db.Query("INSERT INTO Greetings (UserID, Greeting, R, G, B) VALUES (@0, @1, 127, 255, 212)", userid, greet);

            return true;
        }

        private void setGreetRGB(int userid, int[] rgbcolor)
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
        #endregion

        #region LeaveCommands
        private string getLeave(int userid)
        {
            if (hasLeave(userid))
            {
                using (QueryResult reader = db.QueryReader(@"SELECT * FROM Leavings WHERE UserID=@0", userid))
                {
                    if (reader.Read())
                        return reader.Get<string>("Leaving");
                    else
                        return null; //This should never happen
                }
            }
            else
                return null; //This should never happen
        }

        private Color getLeaveRGB(int userid)
        {
            byte[] uacolor = { (byte)127, (byte)255, (byte)212 };

            if (hasLeave(userid))
            {
                using (QueryResult reader = db.QueryReader(@"SELECT * FROM Leavings WHERE UserID=@0", userid))
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

        private bool hasLeave(int userid)
        {
            using (QueryResult reader = db.QueryReader(@"SELECT * FROM Leavings WHERE UserID=@0", userid))
            {
                if (reader.Read())
                    return true;
                else
                    return false;
            }
        }

        private bool setLeave(int userid, string greet, bool ignore)
        {
            if (!ignore)
                foreach (string badword in badwords)
                    if (greet.ToLower().Contains(badword.ToLower()))
                        return false;

            if (hasLeave(userid))
                db.Query("UPDATE Leavings SET Leaving=@0 WHERE UserID=@1", greet, userid);
            else
                db.Query("INSERT INTO Leavings (UserID, Leaving, R, G, B) VALUES (@0, @1, 127, 255, 212)", userid, greet);

            return true;
        }

        private void setLeaveRGB(int userid, int[] rgbcolor)
        {
            if (hasLeave(userid))
            {
                db.Query("UPDATE Leavings SET R=@0 WHERE UserID=@1", rgbcolor[0], userid);
                db.Query("UPDATE Leavings SET G=@0 WHERE UserID=@1", rgbcolor[1], userid);
                db.Query("UPDATE Leavings SET B=@0 WHERE UserID=@1", rgbcolor[2], userid);
            }
        }

        private void removeLeave(int userid)
        {
            if (hasLeave(userid))
                db.Query("DELETE FROM Leavings WHERE UserID=@0", userid);
        }
        #endregion
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
