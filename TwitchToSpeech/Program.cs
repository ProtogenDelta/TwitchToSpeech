using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Speech.Synthesis;
using TwitchLib.Client;

namespace TwitchToSpeech
{
    internal class Program
    {
        private static readonly SpeechSynthesizer ss = new SpeechSynthesizer();
        private static readonly TwitchClient twitchClient = new TwitchClient();
        private static TwitchLib.Client.Models.ChannelState channelState;

        private static readonly Queue<Prompt> prompts = new Queue<Prompt>();
        private static readonly List<string> mutedUsers = new List<string>();
        private static readonly Dictionary<string, string> replacers = new Dictionary<string, string>();

        private static bool silentDisplay = false;
        private static bool silentSpeech = false;
        private static bool speakUsernames = false;

        static void Main(string[] _)
        {
            LoadMutes();
            LoadReplacers();

            var auth = new TwitchLib.Client.Models.ConnectionCredentials("justinfan1", "sad9di9wad");
            twitchClient.Initialize(auth);
            twitchClient.Connect();

            twitchClient.OnJoinedChannel += TwitchClient_OnJoinedChannel;

            twitchClient.OnMessageReceived += TwitchClient_OnMessageReceived;

            twitchClient.OnChannelStateChanged += TwitchClient_OnChannelStateChanged;
            
            twitchClient.OnNewSubscriber += TwitchClient_OnNewSubscriber;
            twitchClient.OnGiftedSubscription += TwitchClient_OnGiftedSubscription;
            twitchClient.OnCommunitySubscription += TwitchClient_OnCommunitySubscription;
            twitchClient.OnRaidNotification += TwitchClient_OnRaidNotification;

            ss.StateChanged += Ss_StateChanged;

            twitchClient.WillReplaceEmotes = true;

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Write("-i- Press [/] to see a list of available shortcuts.", ConsoleColor.DarkGray);

            while(true)
            {
                var key = Console.ReadKey(true);

                if (key.Key == ConsoleKey.Q)
                {
                    // Closes the program
                    silentDisplay = true;
                    Write("-i- Quitting...", ConsoleColor.DarkGray);
                    ss.SpeakAsyncCancelAll();
                    prompts.Clear();
                    twitchClient.Disconnect();
                    break;
                }
                else if (key.Key == ConsoleKey.S)
                {
                    // Toggles TTS
                    silentSpeech = !silentSpeech;
                    var state = silentSpeech ? "disabled" : "enabled";
                    Write("-i- TTS: ", ConsoleColor.DarkGray, false);
                    Write(state, ConsoleColor.Gray);
                }
                else if (key.Key == ConsoleKey.U)
                {
                    // Toggles reading usernames aloud
                    speakUsernames = !speakUsernames;
                    var state = speakUsernames ? "enabled" : "disabled";
                    Write("-i- Speak Usernames: ", ConsoleColor.DarkGray, false);
                    Write(state, ConsoleColor.Gray);
                    Say("usernames " + state);
                }
                else if (key.Key == ConsoleKey.N)
                {
                    // Skips a single message
                    if (prompts.Count > 0)
                    {
                        ss.SpeakAsyncCancel(prompts.Dequeue());
                        Write("-i- Skipped", ConsoleColor.DarkGray);
                    }
                    else Write("-!- Nothing to skip!", ConsoleColor.DarkGray);
                }
                else if (key.Key == ConsoleKey.M)
                {
                    // Stops all queued messages.
                    ss.SpeakAsyncCancelAll();
                    prompts.Clear();
                    Write("-i- Skipped all messages", ConsoleColor.DarkGray);
                }
                else if (key.Key == ConsoleKey.I)
                {
                    // Shows info about the connected channel, if any.
                    if (channelState == default(TwitchLib.Client.Models.ChannelState))
                    {
                        Write("-!- Join a channel first!", ConsoleColor.Red);
                        continue;
                    }
                    Write($"+--- Channel Info: #{channelState.Channel}", ConsoleColor.Cyan);
                    Write($"| ID: {channelState.RoomId}", ConsoleColor.Cyan);
                    Write($"| Language: {channelState.BroadcasterLanguage ?? "Unknown"}", ConsoleColor.Cyan);
                    Write($"| Follower Only: {channelState.FollowersOnly?.ToString() ?? "False"}", ConsoleColor.Cyan);
                    Write($"| Sub Only: {channelState.SubOnly}", ConsoleColor.Cyan);
                    Write($"| Emotes Only: {channelState.EmoteOnly}", ConsoleColor.Cyan);
                    Write($"| Slow Mode: {channelState.SlowMode.ToString() + "s"}", ConsoleColor.Cyan);
                    Write("+---", ConsoleColor.Cyan);
                }
                else if (key.Key == ConsoleKey.C)
                {
                    // Prompts the user for a new channel name and switches to it.
                    silentDisplay = true;
                    Write("-?- Channel: ", ConsoleColor.DarkGray, false);
                    Console.ForegroundColor = ConsoleColor.Gray;
                    string channel = Console.ReadLine();
                    silentDisplay = false;
                    if (string.IsNullOrWhiteSpace(channel)) continue;
                    if(channelState != default(TwitchLib.Client.Models.ChannelState))
                        twitchClient.LeaveChannel(channelState.Channel);
                    twitchClient.JoinChannel(channel);
                }
                else if(key.Key == ConsoleKey.OemPlus)
                {
                    // Prompts the user for a user to mute.
                    silentDisplay = true;
                    Write("-?- Mute: ", ConsoleColor.DarkGray, false);
                    Console.ForegroundColor = ConsoleColor.Gray;
                    string user = Console.ReadLine();
                    silentDisplay = false;
                    if (string.IsNullOrWhiteSpace(user)) continue;
                    MuteUser(user);
                }
                else if (key.Key == ConsoleKey.OemMinus)
                {
                    // Prompts the user for a user to unmute.
                    silentDisplay = true;
                    Write("-?- Unmute: ", ConsoleColor.DarkGray, false);
                    Console.ForegroundColor = ConsoleColor.Gray;
                    string user = Console.ReadLine();
                    silentDisplay = false;
                    if (string.IsNullOrWhiteSpace(user)) continue;
                    UnmuteUser(user);
                }
                else if(key.Key == ConsoleKey.OemPeriod)
                {
                    Write($"+--- Mutes ({mutedUsers.Count})", ConsoleColor.Cyan);
                    mutedUsers.ForEach(user =>
                    {
                        Write($"| {user}", ConsoleColor.Cyan);
                    });
                    Write($"+---", ConsoleColor.Cyan);
                }
                else if (key.Key == ConsoleKey.Oem2) // Slash Key
                {
                    // Behold, an unsightly block of text.
                    Write("+--- Help", ConsoleColor.Cyan);
                    Write("| (q) Quit", ConsoleColor.Cyan);
                    Write("| (s) Toggle TTS", ConsoleColor.Cyan);
                    Write("| (u) Toggle speaking usernames", ConsoleColor.Cyan);
                    Write("| (n) Skip current message", ConsoleColor.Cyan);
                    Write("| (m) Skip all messages", ConsoleColor.Cyan);
                    Write("| (i) Channel info", ConsoleColor.Cyan);
                    Write("| (c) Switch channels", ConsoleColor.Cyan);
                    Write("| (=) Mute User", ConsoleColor.Cyan);
                    Write("| (-) Unmute User", ConsoleColor.Cyan);
                    Write("| (.) List Mutes", ConsoleColor.Cyan);
                    Write("| (/) Help", ConsoleColor.Cyan);
                    Write("+---", ConsoleColor.Cyan);
                }
#if DEBUG
                else
                {
                    Write($"-!- Unknown Keybind: '{key.Key}'", ConsoleColor.DarkGray);
                }
#endif
            }
        }

        private static void MuteUser(string username)
        {
            username = username.ToLower();
            if (mutedUsers.Contains(username))
            {
                Write($"-!- {username} is already muted.", ConsoleColor.Red);
                return;
            }
            mutedUsers.Add(username);
            if(!File.Exists("muted_users.txt")) File.Create("muted_users.txt").Dispose();
            File.WriteAllLines("muted_users.txt", mutedUsers.ToArray());
            Write($"-i- Muted {username}.", ConsoleColor.Yellow);
        }

        private static void UnmuteUser(string username)
        {
            username = username.ToLower();
            if (!mutedUsers.Contains(username))
            {
                Write($"-!- {username} is not muted.", ConsoleColor.Red);
                return;
            }
            mutedUsers.Remove(username);
            if (!File.Exists("muted_users.txt")) File.Create("muted_users.txt").Dispose();
            File.WriteAllLines("muted_users.txt", mutedUsers.ToArray());
            Write($"-i- Unmuted {username}.", ConsoleColor.Yellow);
        }

        private static void LoadMutes()
        {
            // Read all the muted users from a file
            if (!File.Exists("muted_users.txt")) File.Create("muted_users.txt").Dispose();
            var users = File.ReadAllLines("muted_users.txt");
            mutedUsers.AddRange(users);
            Write($"-i- Loaded {mutedUsers.Count} muted users.", ConsoleColor.DarkGray);
        }

        private static void LoadReplacers()
        {
            // Read all the replacers from a file
            if (!File.Exists("replacements.txt")) File.Create("replacements.txt").Dispose();
            var repls = File.ReadAllLines("replacements.txt");
            var replRegex = new Regex(@"(?<from>\S+)|(?<to>\S*)");
            foreach(var repl in repls)
            {
                var match = repl.Split('|');
                if(match.Length != 2)
                {
                    Write($"-!- Invalid Replacer Syntax: {repl}", ConsoleColor.Yellow);
                    continue;
                }
                else replacers.Add(match[0], match[1]);
            }
            Write($"-i- Loaded {replacers.Count} replacers.", ConsoleColor.DarkGray);
        }

        private static void Ss_StateChanged(object sender, StateChangedEventArgs e)
        {
            // Remove all completed speech prompts from the queue
            // note(protogendelta): I don't trust this not to break, but at the same time I don't really care, so :p
            while (prompts.Count > 0 && prompts.Peek().IsCompleted)
            {
                prompts.Dequeue();
            }
        }

        private static void TwitchClient_OnJoinedChannel(object sender, TwitchLib.Client.Events.OnJoinedChannelArgs e)
        {
            if (silentDisplay) return;
            Write($"-i- Joined #{e.Channel}", ConsoleColor.Cyan);
        }

        private static void TwitchClient_OnRaidNotification(object sender, TwitchLib.Client.Events.OnRaidNotificationArgs e)
        {
            if (silentDisplay) return;
            Write($">>> {e.RaidNotification.SystemMsgParsed}", ConsoleColor.Magenta);
            Say(e.RaidNotification.SystemMsgParsed);
        }

        private static void TwitchClient_OnNewSubscriber(object sender, TwitchLib.Client.Events.OnNewSubscriberArgs e)
        {
            if (silentDisplay) return;
            Write($">>> {e.Subscriber.SystemMessageParsed}", ConsoleColor.Magenta);
            Say(e.Subscriber.SystemMessageParsed);
        }

        private static void TwitchClient_OnGiftedSubscription(object sender, TwitchLib.Client.Events.OnGiftedSubscriptionArgs e)
        {
            if (silentDisplay) return;
            Write($">>> {e.GiftedSubscription.SystemMsgParsed}", ConsoleColor.Magenta);
            Say(e.GiftedSubscription.SystemMsgParsed);
        }

        private static void TwitchClient_OnCommunitySubscription(object sender, TwitchLib.Client.Events.OnCommunitySubscriptionArgs e)
        {
            if (silentDisplay) return;
            Write($">>> {e.GiftedSubscription.SystemMsgParsed}", ConsoleColor.Magenta);
            Say(e.GiftedSubscription.SystemMsgParsed);
        }

        private static void TwitchClient_OnChannelStateChanged(object sender, TwitchLib.Client.Events.OnChannelStateChangedArgs e)
        {
            channelState = e.ChannelState;
        }

        private static void TwitchClient_OnMessageReceived(object sender, TwitchLib.Client.Events.OnMessageReceivedArgs e)
        {
            if (silentDisplay) return;
            var nameColor = ConsoleColor.White;
            var msgColor = ConsoleColor.Gray;
            var msg = e.ChatMessage;
            if(msg.IsBroadcaster) {nameColor = ConsoleColor.Red;}
            else if(msg.IsModerator) {nameColor = ConsoleColor.Green;}
            else if(msg.IsSubscriber) {nameColor = ConsoleColor.Magenta;}
            if(msg.IsHighlighted) {msgColor = ConsoleColor.Blue;}
            else if(msg.Bits != 0) {msgColor = ConsoleColor.DarkYellow;}
            Write($"{msg.Username}: ", nameColor, false);
            Write(msg.Message, msgColor);
            if (!string.IsNullOrEmpty(msg.EmoteReplacedMessage))
            {
                var cleaned = Regex.Replace(msg.EmoteReplacedMessage, @"http[^\s]+\s?", "");
                if (!string.IsNullOrEmpty(cleaned)) SayMessage(msg.Username, cleaned);
            }
            else
                SayMessage(msg.Username, msg.Message);
        }

        private static void SayMessage(string username, string text)
        {
            if(new Regex(@"^[!].*").IsMatch(text.TrimStart()))
            {
#if DEBUG
                Write("-~- Command; skipping...", ConsoleColor.Yellow);
#endif
                return;
            }
            if(mutedUsers.Contains(username))
            {
#if DEBUG
                Write($"-~- {username} is muted.", ConsoleColor.Yellow);
#endif
                return;
            }
            if (speakUsernames)
                Say($"{username} says: {text}");
            else
                Say(text);
        }

        private static void Say(string text)
        {
            if(silentSpeech) return;
            var words = text.Split(' ');
            List<string> outwords = new List<string>();
            foreach(var word in words)
            {
                if (replacers.TryGetValue(word, out string value))
                {
                    outwords.Add(value);
                }
                else outwords.Add(word);
            }
            prompts.Enqueue(ss.SpeakAsync(string.Join(" ", outwords.ToArray())));
        }

        private static void Write(string text, ConsoleColor color, bool newline = true)
        {
            Console.ForegroundColor = color;
            if(newline)
                Console.WriteLine(text);
            else
                Console.Write(text);
        }
    }
}