using System;
using System.Threading;
using System.Net.Sockets;
using System.Text;

namespace GepotServer
{
    public enum PlayerState
    {
        ReadyToPlay = 0,
        RequestedOtherPlayer = 1, // FIXME: implement
        OtherPlayerRequestsMe = 2,
        InGame = 3,
    };

    public class Player
    {
        public string ip_addr;
        public int port;
        public string name;
        public string version;
        public string hash;
        //public string skill;
        // Skill field
        public uint wins = 0;
        public uint losses = 0;
        public uint draws = 0;
        // State
        public PlayerState state;
        public Mutex sockMutex = new Mutex();
        private TcpClient l;
        public Player partner = null;
        public Mutex partnerMutex = new Mutex();
        public Player(TcpClient l_, string ip_addr_, int port_, string name_, string version_, string hash_)
        {
            l = l_;
            ip_addr = ip_addr_;
            port = port_;
            name = name_;
            version = version_;
            hash = hash_;
            //skill = "0/0/0/0/0";
            state = PlayerState.ReadyToPlay;
        }
        public void WriteShared(ref byte[] data)
        {
            sockMutex.WaitOne();
            Write(ref data);
            sockMutex.ReleaseMutex();
        }
        public void WriteShared(string data)
        {
            byte[] b = Encoding.UTF8.GetBytes(data);
            WriteShared(ref b);
        }
        public void Write(ref byte[] data)
        {
            NetworkStream ns = l.GetStream();
            byte[] terminatedData = new byte[data.Length + 1];
            Buffer.BlockCopy(data, 0, terminatedData, 0, data.Length);
            terminatedData[data.Length] = (byte)'\0';
            ns.Write(terminatedData, 0, terminatedData.Length);
        }
        public void Write(string data)
        {
            byte[] b = Encoding.UTF8.GetBytes(data);
            Write(ref b);
        }
        public string GetStateString()
        {
            return state.ToString("D");
        }
        public string GetSkillString()
        {
            return $"{wins}/{losses}/{draws}/0/0";
        }
    }
}
