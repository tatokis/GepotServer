using System;
using System.Xml;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using System.Text;

namespace GepotServer
{
    class MainClass
    {
        private static List<Player> players = new List<Player>();
        private static Mutex playerListMutex = new Mutex();

        // Set to false to disable chat
        private static readonly bool chatEnabled = true;

        private static void sendNewPlayerMsg(ref Player p)
        {
            XmlDocument doc = new XmlDocument();
            XmlElement root = doc.CreateElement("newPlayer");
            root.SetAttribute("name", p.name);
            root.SetAttribute("skill", p.GetSkillString());
            root.SetAttribute("state", p.GetStateString());
            doc.InsertBefore(root, doc.DocumentElement);
            byte[] xml = Encoding.UTF8.GetBytes(doc.OuterXml);
            foreach (Player pl in players)
            {
                if (pl == p)
                    continue;
                pl.WriteShared(ref xml);
            }
        }

        // Call this with the player list mutex locked
        private static byte[] generateUserList()
        {
            XmlDocument doc = new XmlDocument();
            XmlElement root = doc.CreateElement("userList");
            foreach (Player p in players)
            {
                XmlElement playerElement = doc.CreateElement("player");
                playerElement.SetAttribute("name", p.name);
                playerElement.SetAttribute("skill", p.GetSkillString());
                playerElement.SetAttribute("state", p.GetStateString());
                root.AppendChild(playerElement);
            }
            doc.InsertBefore(root, doc.DocumentElement);
            return Encoding.UTF8.GetBytes(doc.OuterXml);
        }

        private static void sendPlayerLeftMsg(ref Player p)
        {
            XmlDocument doc = new XmlDocument();
            XmlElement root = doc.CreateElement("playerLeft");
            root.SetAttribute("name", p.name);
            doc.InsertBefore(root, doc.DocumentElement);

            byte[] msg = Encoding.UTF8.GetBytes(doc.OuterXml);

            playerListMutex.WaitOne();
            foreach (Player pl in players)
            {
                pl.WriteShared(ref msg);
            }
            playerListMutex.ReleaseMutex();
        }

        // Main client loop
        public static void mainLoop(object param)
        {
            TcpClient c = (TcpClient)param;
            NetworkStream ns = c.GetStream();
            IPEndPoint e = (IPEndPoint)c.Client.RemoteEndPoint;

            string addr = e.Address.ToString();
            int port = e.Port;

            Player currentPlayer = null;

            Console.WriteLine("New client connected from ");
            while (c.Connected)
            {
                Byte[] data = new byte[2048];
                int ret = ns.Read(data, 0, 2048);
                // Detect client disconnection
                if (ret == 0)
                    break;

                // Split and remove the null terminator
                string[] msgs = Encoding.UTF8.GetString(data).Split(new[] {'\0'}, StringSplitOptions.RemoveEmptyEntries);
                foreach (string m in msgs)
                {
                    bool tagStartsWithNum = false;
                    string msg = m;

                    // If the XML tag starts with a number, add a dummy character to make it valid
                    if (msg[0] == '<' && Char.IsNumber(msg[1]))
                    {
                        msg = msg.Insert(1, "a");
                        tagStartsWithNum = true;
                    }

                    // Parse it
                    XmlDocument doc = new XmlDocument();
                    doc.LoadXml(msg);

                    Console.Write(msg);
                    XmlElement root = doc.DocumentElement;
                    string rname = root.Name;
                    if (rname == "policy-file-request")
                    {
                        // In only this case, we have to explicitly close the connection
                        // Otherwise the game doesn't do anything
                        byte[] bytes = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>\n<!DOCTYPE cross-domain-policy SYSTEM \"/xml/dtds/cross-domain-policy.dtd\">\n<cross-domain-policy>\n   <site-control permitted-cross-domain-policies=\"master-only\"/>\n   <allow-access-from domain=\"*\" to-ports=\"*\" />\n</cross-domain-policy>");
                        ns.Write(bytes, 0, bytes.Length);
                        c.Close();
                        break;
                    }
                    else if (rname == "auth")
                    {
                        // Extract the fields
                        string name = root.GetAttribute("name");
                        string ver = root.GetAttribute("version");
                        // Hash is not implemented. It was originally used for verification so that one couldn't put the game on another site.
                        string hash = root.GetAttribute("hash");
                        Console.WriteLine($"Auth tag received {name} {ver} {hash}");

                        // Create the user
                        playerListMutex.WaitOne();

                        // Add the user to the list
                        players.Add(new Player(c, addr, port, name, ver, hash));

                        // Update the reference
                        currentPlayer = players[players.Count - 1];

                        // Tell everyone else that the user joined
                        sendNewPlayerMsg(ref currentPlayer);

                        currentPlayer.Write(@"<config adConfigUrl="""" badWordsUrl=""www.speeleiland.nl/bad_words/badwords.html"" replacementChar=""*"" deleteLine=""true"" floodLimit=""2000""/>");

                        // Now send the player list to this player themselves
                        byte[] b = generateUserList();
                        playerListMutex.ReleaseMutex();

                        currentPlayer.Write(ref b);
                    }
                    else if (rname == "beat")
                    {
                        Console.WriteLine($"Beat from {currentPlayer.port}");
                        continue;
                    }
                    else if (rname == "challenge" || rname == "remChallenge" || rname == "startGame") // challenge is what the client sends to the server to ask another player to join, remChallenge to undo, startGame to start a game
                    {
                        string target = root.GetAttribute("name");
                        // string targetHash = root.GetAttribute("hash"); // This is always "xxxxxx" for some reason

                        playerListMutex.WaitOne();
                        // Find the target and send the message
                        foreach (Player p in players)
                        {
                            if (p.name != target)
                                continue;

                            // Generate the "request" XML, to be sent to the target player
                            // It's remRequest if we are removing a challenge
                            // startGame starts the game
                            string tagName = (rname.StartsWith("rem") ? "remRequest" : (rname.StartsWith("start") ? "startGame" : "request"));
                            XmlDocument reqDoc = new XmlDocument();
                            XmlElement reqRoot = reqDoc.CreateElement(tagName);
                            // We set our name here, to send to the other player
                            reqRoot.SetAttribute("name", currentPlayer.name);
                            reqDoc.InsertBefore(reqRoot, reqDoc.DocumentElement);
                            byte[] xml = Encoding.UTF8.GetBytes(reqDoc.OuterXml);
                            // Send it
                            p.WriteShared(ref xml);
                            // Store the partner for each object
                            p.partnerMutex.WaitOne();
                            currentPlayer.partnerMutex.WaitOne();
                            p.partner = currentPlayer;
                            currentPlayer.partner = p;

                            // If the game is starting, set both player states to InGame
                            if (rname.StartsWith("start"))
                            {
                                p.state = p.partner.state = PlayerState.InGame;
                            }

                            currentPlayer.partnerMutex.ReleaseMutex();
                            p.partnerMutex.ReleaseMutex();
                            break;
                        }
                        playerListMutex.ReleaseMutex();
                    }
                    else if (rname == "winGame")
                    {
                        // FIXME: Update player stats here
                        currentPlayer.wins++;
                        currentPlayer.partnerMutex.WaitOne();
                        currentPlayer.partner.losses++;
                        currentPlayer.partnerMutex.ReleaseMutex();
                        Console.WriteLine($"{currentPlayer.name} won!");
                    }
                    else if (rname == "surrender")
                    {
                        // Add the name of the person who won and send it to both players
                        currentPlayer.partnerMutex.WaitOne();
                        root.SetAttribute("winner", currentPlayer.partner.name);
                        byte[] xml = Encoding.UTF8.GetBytes(root.OuterXml);

                        currentPlayer.partner.WriteShared(ref xml);
                        currentPlayer.partnerMutex.ReleaseMutex();
                        currentPlayer.Write(ref xml);
                    }
                    else if (rname == "playAgain" || rname == "msgPlayer") // Passthrough commands
                    {
                        byte[] xml = Encoding.UTF8.GetBytes(root.OuterXml);

                        // Forward the message to our partner
                        currentPlayer.partnerMutex.WaitOne();
                        currentPlayer.partner.WriteShared(ref xml);
                        currentPlayer.partnerMutex.ReleaseMutex();
                    }
                    else if (rname == "msgAll")
                    {
                        if (!chatEnabled)
                        {
                            root.SetAttribute("name", "server");
                            root.SetAttribute("msg", "Sorry! Chat is disabled!");
                        }
                        // These are sent to everyone
                        byte[] xml = Encoding.UTF8.GetBytes(root.OuterXml);
                        if (chatEnabled)
                        {
                            playerListMutex.WaitOne();
                            foreach (Player p in players)
                            {
                                // Ignore ourselves
                                if (p == currentPlayer)
                                    continue;
                                p.WriteShared(ref xml);
                            }
                            playerListMutex.ReleaseMutex();
                            continue;
                        }
                        // Unless of course, the chat is disabled.
                        // Then we complain only to the user who attempted to send the message.
                        currentPlayer.Write(ref xml);

                    }
                    else if (rname == "toRoom")
                    {
                        // Reset the state
                        currentPlayer.state = PlayerState.ReadyToPlay;
                    }
                    else if (rname[0] == 'a' && tagStartsWithNum) // This should be near the end of the if/elseif/else chain
                    {
                        // Strip out the character
                        rname = rname.TrimStart('a');

                        // Recreate the tag and copy the data over
                        XmlDocument reqDoc = new XmlDocument();
                        XmlElement reqRoot = reqDoc.CreateElement(rname);

                        // Pass the appropriate attributes
                        if (rname == "12" || rname == "11")
                        {
                            reqRoot.SetAttribute("r", root.GetAttribute("r"));
                            reqRoot.SetAttribute("p", root.GetAttribute("p"));
                            if (rname == "11")
                            {
                                reqRoot.SetAttribute("ex", root.GetAttribute("ex"));
                                reqRoot.SetAttribute("ey", root.GetAttribute("ey"));
                            }
                        }
                        // Pass everything else through (such as 13)
                        reqDoc.InsertBefore(reqRoot, reqDoc.DocumentElement);
                        byte[] xml = Encoding.UTF8.GetBytes(reqDoc.OuterXml);

                        // Forward the message to our partner
                        currentPlayer.partnerMutex.WaitOne();
                        currentPlayer.partner.WriteShared(ref xml);
                        currentPlayer.partnerMutex.ReleaseMutex();
                    }
                    else
                    {
                        Console.WriteLine("Unknown tag " + rname);
                    }
                }
            }
            Console.WriteLine("Client disconnected");
            if(currentPlayer != null)
            {
                // Remove player from the list
                players.Remove(currentPlayer);

                // Tell everyone else that the player was removed
                sendPlayerLeftMsg(ref currentPlayer);
            }
        }

        public static void Main(string[] args)
        {
            // Start the server
            int port = 6890;
            TcpListener l = new TcpListener(System.Net.IPAddress.Any, port);
            l.Start();
            Console.WriteLine("Server Started");
            while (true)
            {
                TcpClient c = l.AcceptTcpClient();
                Thread t = new Thread(mainLoop);
                t.Start(c);
            }
        }
    }
}
