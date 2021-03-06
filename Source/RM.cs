#region using namespaces
using System;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Collections;
using System.Net.Sockets;
using BitTorrent;
using BytesRoad.Net.Sockets;
using System.IO;
using System.Threading;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Net;
#endregion
namespace RatioMaster_source
{
    internal partial class RM : UserControl
    {
        // Variables
        #region Variables
        private bool getnew = true;
        private readonly Random rand = new Random(((int) DateTime.Now.Ticks));
        private int remWork = 0;
        internal string DefaultDirectory = "";
        private const string DefaultClient = "uTorrent";
        private const string DefaultClientVersion = "3.3.0";
        //internal delegate SocketEx createSocketCallback();
        internal delegate void SetTextCallback(string logLine);
        internal delegate void updateScrapCallback(string seedStr, string leechStr, string finishedStr);
        private TorrentClient currentClient;
        private ProxyInfo currentProxy;
        internal TorrentInfo currentTorrent = new TorrentInfo();
        internal Torrent currentTorrentFile = new Torrent();
        internal TcpListener localListen;
        private bool seedMode = false;
        private long overHeadData;
        private bool updateProcessStarted = false;
        private bool requestScrap;
        private bool scrapStatsUpdated;
        private int temporaryIntervalCounter = 0;
        private readonly RandomStringGenerator stringGenerator = new RandomStringGenerator();
        bool IsExit = false;
        private readonly string version = "";
        #endregion
        // Methods
        #region Methods
        #region Main Form Events
        internal RM()
        {
            InitializeComponent();
            deployDefaultValues();
            GetPCinfo();
            ReadSettings();
        }
        internal void Form1_DragDrop(object sender, DragEventArgs e)
        {
            string[] s = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (s == null)
            {
                s = (string[])e.Data.GetData("System.String[]", true);
                if (s == null)
                {
                    return;
                }
            }
            loadTorrentFileInfo(s[0]);
        }
        internal void Form1_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text) || e.Data.GetFormats().ToString().Equals("System.String[]"))
            {
                e.Effect = DragDropEffects.All;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }
        internal void ExitRatioMaster()
        {
            IsExit = true;
            if (updateProcessStarted)
            {
                @StopButton_Click(null, null);
            }
            //this.Close();
            //Process.GetCurrentProcess().Kill();
            //Application.Exit();
        }
        internal void deployDefaultValues()
        {
            TorrentInfo torrent = new TorrentInfo(0, 0);
            trackerAddress.Text = torrent.tracker;
            shaHash.Text = torrent.hash;
            long num1 = torrent.uploadRate / 1024;
            uploadRate.Text = num1.ToString();
            long num2 = torrent.downloadRate / 1024;
            downloadRate.Text = num2.ToString();
            interval.Text = torrent.interval.ToString();
            comboProxyType.SelectedItem = "None";
        }
        #endregion
        #region Log code
        internal void AddClientInfo()
        {
            // Add log info
            AddLogLine("CLIENT EMULATION INFO:");
            AddLogLine("Name: " + currentClient.Name);
            AddLogLine("HttpProtocol: " + currentClient.HttpProtocol);
            AddLogLine("HashUpperCase: " + currentClient.HashUpperCase);
            AddLogLine("Key: " + currentClient.Key);
            AddLogLine("Headers:......");
            AddLog(currentClient.Headers);
            AddLogLine("PeerID: " + currentClient.PeerID);
            AddLogLine("Query: " + currentClient.Query + "\n" + "\n");
        }
        internal void AddLog(string logLine)
        {
            if (logWindow.InvokeRequired)
            {
                SetTextCallback d = AddLogLine;
                Invoke(d, new object[] { logLine });
            }
            else
            {
                if (checkLogEnabled.Checked && IsExit != true)
                {
                    try
                    {
                        logWindow.AppendText(logLine);
                        //logWindow.SelectionStart = logWindow.Text.Length;
                        logWindow.ScrollToCaret();
                    }
                    catch (Exception) { }
                }
            }
        }
        internal void AddLogLine(string logLine)
        {
            if (logWindow.InvokeRequired && IsExit != true)
            {
                SetTextCallback d = AddLogLine;
                Invoke(d, new object[] { logLine });
            }
            else
            {
                if (checkLogEnabled.Checked)
                {
                    try
                    {
                        DateTime dtNow = DateTime.Now;
                        string dateString = "[" + String.Format("{0:hh:mm:ss}", dtNow) + "]";
                        logWindow.AppendText(dateString + " " + logLine + "\r\n");
                        //logWindow.SelectionStart = logWindow.Text.Length;
                        logWindow.ScrollToCaret();
                    }
                    catch (Exception) { }
                }
            }
        }
        internal void ClearLog()
        {
            logWindow.Clear();
        }
        internal void GetPCinfo()
        {
            try
            {
                AddLogLine("CurrentDirectory: " + Environment.CurrentDirectory);
                AddLogLine("HasShutdownStarted: " + Environment.HasShutdownStarted);
                AddLogLine("MachineName: " + Environment.MachineName);
                AddLogLine("OSVersion: " + Environment.OSVersion);
                AddLogLine("ProcessorCount: " + Environment.ProcessorCount);
                AddLogLine("UserDomainName: " + Environment.UserDomainName);
                AddLogLine("UserInteractive: " + Environment.UserInteractive);
                AddLogLine("UserName: " + Environment.UserName);
                AddLogLine("Version: " + Environment.Version);
                AddLogLine("WorkingSet: " + Environment.WorkingSet);
                AddLogLine("");
            }
            catch (Exception) { }
        }
        internal void SaveLog_FileOk(object sender, CancelEventArgs e)
        {
            string file = SaveLog.FileName;
            StreamWriter sw = new StreamWriter(file);
            sw.Write(logWindow.Text);
            sw.Close();
        }
        #endregion
        #region Tcp Listener code
        private void OpenTcpListener()
        {
            try
            {
                if (checkTCPListen.Checked && localListen == null && currentProxy.proxyType == ProxyType.None)
                {
                    localListen = new TcpListener(int.Parse(currentTorrent.port));
                    try
                    {
                        localListen.Start();
                        AddLogLine("Started TCP listener on port " + currentTorrent.port);
                    }
                    catch
                    {
                        AddLogLine("TCP listener is alredy started from other torrent or from your torrent client");
                        return;
                    }
                    Thread myThread = new Thread(AcceptTcpConnection);
                    myThread.Name = "AcceptTcpConnection() Thread";
                    myThread.Start();
                }
            }
            catch (Exception e)
            {
                AddLogLine("Error in OpenTcpListener(): " + e.Message);
                if (localListen != null)
                {
                    localListen.Stop();
                    localListen = null;
                }
                return;
            }
            AddLogLine("OpenTcpListener() successfully finished!");
        }
        private void AcceptTcpConnection()
        {
            Socket socket1 = null;
            try
            {
                Encoding encoding1 = Encoding.GetEncoding(0x6faf);
                string text1;
                while (true)
                {
                    socket1 = localListen.AcceptSocket();
                    byte[] buffer1 = new byte[0x43];
                    if ((socket1 != null) && socket1.Connected)
                    {
                        NetworkStream stream1 = new NetworkStream(socket1);
                        stream1.ReadTimeout = 0x3e8;
                        try
                        {
                            stream1.Read(buffer1, 0, buffer1.Length);
                        }
                        catch (Exception)
                        {
                        }
                        text1 = encoding1.GetString(buffer1, 0, buffer1.Length);
                        if ((text1.IndexOf("BitTorrent protocol") >= 0) && (text1.IndexOf(encoding1.GetString(this.currentTorrentFile.InfoHash)) >= 0))
                        {
                            byte[] buffer2 = createHandshakeResponse();
                            stream1.Write(buffer2, 0, buffer2.Length);
                        }
                        socket1.Close();
                        stream1.Close();
                        stream1.Dispose();
                    }
                }
            }
            catch (Exception exception1)
            {
                AddLogLine("Error in AcceptTcpConnection(): " + exception1.Message);
                return;
            }
            finally
            {
                if (socket1 != null)
                {
                    socket1.Close();
                    AddLogLine("Closed socket");
                }
                CloseTcpListener();
            }
        }
        private Socket createRegularSocket()
        {
            Socket socket1 = null;
            try
            {
                socket1 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            }
            catch (Exception exception1)
            {
                AddLogLine("createSocket error: " + exception1.Message);
            }
            return socket1;
        }
        private byte[] createChokeResponse()
        {
            byte[] buffer2 = new byte[5];
            buffer2[3] = 1;
            return buffer2;
        }
        private byte[] createHandshakeResponse()
        {
            int num1 = 0;
            Encoding encoding1 = Encoding.GetEncoding(0x6faf);
            new StringBuilder();
            string text1 = "BitTorrent protocol";
            byte[] buffer1 = new byte[0x100];
            buffer1[num1++] = (byte)text1.Length;
            encoding1.GetBytes(text1, 0, text1.Length, buffer1, num1);
            num1 += text1.Length;
            for (int num2 = 0; num2 < 8; num2++)
            {
                buffer1[num1++] = 0;
            }
            Buffer.BlockCopy(currentTorrentFile.InfoHash, 0, buffer1, num1, currentTorrentFile.InfoHash.Length);
            num1 += currentTorrentFile.InfoHash.Length;
            encoding1.GetBytes(currentTorrent.peerID.ToCharArray(), 0, currentTorrent.peerID.Length, buffer1, num1);
            num1 += encoding1.GetByteCount(currentTorrent.peerID);
            return buffer1;
        }
        internal void CloseTcpListener()
        {
            if (localListen != null)
            {
                localListen.Stop();
                localListen = null;
                AddLogLine("TCP Listener closed");
            }
        }
        #endregion
        #region Get client
        internal string GetClientName()
        {
            return cmbClient.SelectedItem + " " + cmbVersion.SelectedItem;
        }
        private TorrentClient getCurrentClient(string name)
        {
            TorrentClient client = new TorrentClient(name);
            switch (name)
            {
                #region BitComet
                case "BitComet 1.20":
                    {
                        client.Name = "BitComet 1.20";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = true;
                        client.Key = GenerateIdString("numeric", 5, false, false);
                        client.Headers = "Host: {host}\r\nConnection: close\r\nAccpet: */*\r\nAccept-Encoding: gzip\r\nUser-Agent: BitComet/1.20.3.25\r\nPragma: no-cache\r\nCache-Control: no-cache\r\n";
                        client.PeerID = "-BC0120-" + GenerateIdString("random", 12, true, true);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&natmapped=1&localip={localip}&port_type=wan&uploaded={uploaded}&downloaded={downloaded}&left={left}&numwant={numwant}&compact=1&no_peer_id=1&key={key}{event}";
                        client.DefNumWant = 200;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-BC0120-";
                        client.ProcessName = "BitComet";
                        client.StartOffset = 0;
                        client.MaxOffset = 60000000;
                        break;
                    }
                case "BitComet 1.03":
                    {
                        client.Name = "BitComet 1.03";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = true;
                        client.Key = GenerateIdString("numeric", 5, false, false);
                        client.Headers = "Host: {host}\r\nConnection: close\r\nAccpet: */*\r\nAccept-Encoding: gzip\r\nUser-Agent: BitComet/1.3.7.17\r\nPragma: no-cache\r\nCache-Control: no-cache\r\n";
                        client.PeerID = "-BC0103-" + GenerateIdString("random", 12, true, true);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&natmapped=1&localip={localip}&port_type=wan&uploaded={uploaded}&downloaded={downloaded}&left={left}&numwant={numwant}&compact=1&no_peer_id=1&key={key}{event}";
                        client.DefNumWant = 200;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-BC0103-";
                        client.ProcessName = "BitComet";
                        client.StartOffset = 0;
                        client.MaxOffset = 60000000;
                        break;
                    }
                case "BitComet 0.98":
                    {
                        client.Name = "BitComet 0.98";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = true;
                        client.Key = GenerateIdString("numeric", 5, false, false);
                        client.Headers = "Accept: */*\r\nAccept-Encoding: gzip\r\nConnection: close\r\nHost: {host}\r\nUser-Agent: Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.0; .NET CLR 1.1.4322)\r\n";
                        client.PeerID = "-BC0098-" + GenerateIdString("random", 12, true, true);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&natmapped=1&localip={localip}&port_type=wan&uploaded={uploaded}&downloaded={downloaded}&left={left}&numwant={numwant}&compact=1&no_peer_id=1&key={key}{event}";
                        client.DefNumWant = 200;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-BC0098-";
                        client.ProcessName = "BitComet";
                        client.StartOffset = 0;
                        client.MaxOffset = 60000000;
                        break;
                    }
                case "BitComet 0.96":
                    {
                        client.Name = "BitComet 0.96";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = true;
                        client.Key = GenerateIdString("numeric", 5, false, false);
                        client.Headers = "Accept: */*\r\nAccept-Encoding: gzip\r\nConnection: close\r\nHost: {host}\r\nUser-Agent: Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.0; .NET CLR 1.1.4322)\r\n";
                        client.PeerID = "-BC0096-" + GenerateIdString("random", 12, true, true);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&natmapped=1&localip={localip}&port_type=wan&uploaded={uploaded}&downloaded={downloaded}&left={left}&numwant={numwant}&compact=1&no_peer_id=1&key={key}{event}";
                        client.DefNumWant = 200;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-BC0096-";
                        client.ProcessName = "BitComet";
                        client.StartOffset = 0;
                        client.MaxOffset = 60000000;
                        break;
                    }
                case "BitComet 0.93":
                    {
                        client.Name = "BitComet 0.93";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = true;
                        client.Key = GenerateIdString("numeric", 5, false, false);
                        client.Headers = "Accept: */*\r\nAccept-Encoding: gzip\r\nConnection: close\r\nHost: {host}\r\nUser-Agent: Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.0; .NET CLR 1.1.4322)\r\n";
                        client.PeerID = "-BC0093-" + GenerateIdString("random", 12, true, true);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&natmapped=1&localip={localip}&port_type=wan&uploaded={uploaded}&downloaded={downloaded}&left={left}&numwant={numwant}&compact=1&no_peer_id=1&key={key}{event}";
                        client.DefNumWant = 200;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-BC0093-";
                        client.ProcessName = "BitComet";
                        client.StartOffset = 0;
                        client.MaxOffset = 60000000;
                        break;
                    }
                case "BitComet 0.92":
                    {
                        client.Name = "BitComet 0.92";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = true;
                        client.Key = GenerateIdString("numeric", 5, false, false);
                        client.Headers = "Accept: */*\r\nAccept-Encoding: gzip\r\nConnection: close\r\nHost: {host}\r\nUser-Agent: Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.0; .NET CLR 1.1.4322)\r\n";
                        client.PeerID = "-BC0092-" + GenerateIdString("random", 12, true, true);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&natmapped=1&localip={localip}&port_type=wan&uploaded={uploaded}&downloaded={downloaded}&left={left}&numwant={numwant}&compact=1&no_peer_id=1&key={key}{event}";
                        client.DefNumWant = 200;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-BC0092-";
                        client.ProcessName = "BitComet";
                        client.StartOffset = 0;
                        client.MaxOffset = 60000000;
                        break;
                    }
                #endregion
                #region Vuze
                case "Vuze 4.2.0.8":
                    {
                        client.Name = "Vuze 4.2.0.8";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = true;
                        client.Key = GenerateIdString("alphanumeric", 8, false, false);
                        client.Headers = "User-Agent: Azureus 4.2.0.8;Windows XP;Java 1.6.0_05\r\nConnection: close\r\nAccept-Encoding: gzip\r\nHost: {host}\r\nAccept: text/html, image/gif, image/jpeg, *; q=.2, */*; q=.2\r\n";
                        client.PeerID = "-AZ4208-" + GenerateIdString("alphanumeric", 12, false, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&supportcrypto=1&port={port}&azudp={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&corrupt=0{event}&numwant={numwant}&no_peer_id=1&compact=1&key={key}&azver=3";
                        client.DefNumWant = 50;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-AZ4208-";
                        client.ProcessName = "azureus";
                        client.StartOffset = 0;
                        client.MaxOffset = 100000000;
                        break;
                    }
                #endregion
                #region Azureus
                case "Azureus 3.1.1.0":
                    {
                        client.Name = "Azureus 3.1.1.0";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = true;
                        client.Key = GenerateIdString("alphanumeric", 8, false, false);
                        client.Headers = "User-Agent: Azureus 3.1.1.0;Windows XP;Java 1.6.0_07\r\nConnection: close\r\nAccept-Encoding: gzip\r\nHost: {host}\r\nAccept: text/html, image/gif, image/jpeg, *; q=.2, */*; q=.2\r\n";
                        client.PeerID = "-AZ3110-" + GenerateIdString("alphanumeric", 12, false, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&supportcrypto=1&port={port}&azudp={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&corrupt=0{event}&numwant={numwant}&no_peer_id=1&compact=1&key={key}&azver=3";
                        client.DefNumWant = 50;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-AZ3110-";
                        client.ProcessName = "Azureus";
                        client.StartOffset = 0;
                        client.MaxOffset = 100000000;
                        break;
                    }
                case "Azureus 3.0.5.0":
                    {
                        client.Name = "Azureus 3.0.5.0";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = true;
                        client.Key = GenerateIdString("alphanumeric", 8, false, false);
                        client.Headers = "User-Agent: Azureus 3.0.5.0;Windows XP;Java 1.6.0_05\r\nConnection: close\r\nAccept-Encoding: gzip\r\nHost: {host}\r\nAccept: text/html, image/gif, image/jpeg, *; q=.2, */*; q=.2\r\nContent-type: application/x-www-form-urlencoded\r\n";
                        client.PeerID = "-AZ3050-" + GenerateIdString("alphanumeric", 12, false, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&supportcrypto=1&port={port}&azudp={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&corrupt=0{event}&numwant={numwant}&no_peer_id=1&compact=1&key={key}&azver=3";
                        client.DefNumWant = 50;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-AZ3050-";
                        client.ProcessName = "Azureus";
                        client.StartOffset = 0;
                        client.MaxOffset = 100000000;
                        break;
                    }
                case "Azureus 3.0.4.2":
                    {
                        client.Name = "Azureus 3.0.4.2";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = true;
                        client.Key = GenerateIdString("alphanumeric", 8, false, false);
                        client.Headers = "User-Agent: Azureus 3.0.4.2;Windows XP;Java 1.5.0_07\r\nConnection: close\r\nAccept-Encoding: gzip\r\nHost: {host}\r\nAccept: text/html, image/gif, image/jpeg, *; q=.2, */*; q=.2\r\nContent-type: application/x-www-form-urlencoded\r\n";
                        client.PeerID = "-AZ3042-" + GenerateIdString("alphanumeric", 12, false, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&supportcrypto=1&port={port}&azudp={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&corrupt=0{event}&numwant={numwant}&no_peer_id=1&compact=1&key={key}&azver=3";
                        client.DefNumWant = 50;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-AZ3042-";
                        client.ProcessName = "Azureus";
                        client.StartOffset = 0;
                        client.MaxOffset = 100000000;
                        break;
                    }
                case "Azureus 3.0.3.4":
                    {
                        client.Name = "Azureus 3.0.3.4";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = true;
                        client.Key = GenerateIdString("alphanumeric", 8, false, false);
                        client.Headers = "User-Agent: Azureus 3.0.3.4;Windows XP;Java 1.6.0_03\r\nConnection: close\r\nAccept-Encoding: gzip\r\nHost: {host}\r\nAccept: text/html, image/gif, image/jpeg, *; q=.2, */*; q=.2\r\n";
                        client.PeerID = "-AZ3034-" + GenerateIdString("alphanumeric", 12, false, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&supportcrypto=1&port={port}&azudp={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&corrupt=0{event}&numwant={numwant}&no_peer_id=1&compact=1&key={key}&azver=3";
                        client.DefNumWant = 50;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-AZ3034-";
                        client.ProcessName = "Azureus";
                        client.StartOffset = 0;
                        client.MaxOffset = 100000000;
                        break;
                    }
                case "Azureus 3.0.2.2":
                    {
                        client.Name = "Azureus 3.0.2.2";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = true;
                        client.Key = GenerateIdString("alphanumeric", 8, false, false);
                        client.Headers = "User-Agent: Azureus 3.0.2.2;Windows XP;Java 1.6.0_01\r\nConnection: close\r\nAccept-Encoding: gzip\r\nHost: {host}\r\nAccept: text/html, image/gif, image/jpeg, *; q=.2, */*; q=.2\r\n";
                        client.PeerID = "-AZ3022-" + GenerateIdString("alphanumeric", 12, false, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&supportcrypto=1&port={port}&azudp={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&corrupt=0{event}&numwant={numwant}&no_peer_id=1&compact=1&key={key}&azver=3";
                        client.DefNumWant = 50;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-AZ3022-";
                        client.ProcessName = "Azureus";
                        client.StartOffset = 0;
                        client.MaxOffset = 100000000;
                        break;
                    }
                case "Azureus 2.5.0.4":
                    {
                        client.Name = "Azureus 2.5.0.4";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = true;
                        client.Key = GenerateIdString("alphanumeric", 8, false, false);
                        client.Headers = "User-Agent: Azureus 2.5.0.4;Windows XP;Java 1.5.0_10\r\nConnection: close\r\nAccept-Encoding: gzip\r\nHost: {host}\r\nAccept: text/html, image/gif, image/jpeg, *; q=.2, */*; q=.2\r\nContent-type: application/x-www-form-urlencoded\r\n";
                        client.PeerID = "-AZ2504-" + GenerateIdString("alphanumeric", 12, false, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&supportcrypto=1&port={port}&azudp={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}{event}&numwant={numwant}&no_peer_id=1&compact=1&key={key}&azver=3";
                        client.DefNumWant = 50;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-AZ2504-";
                        client.ProcessName = "Azureus";
                        client.StartOffset = 0;
                        client.MaxOffset = 100000000;
                        break;
                    }
                #endregion
                #region uTorrent
                case "uTorrent 3.3.0":
                    {
                        client.Name = "uTorrent 3.3.0";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = false;
                        client.Key = GenerateIdString("hex", 8, false, true);
                        client.Headers = "Host: {host}\r\nUser-Agent: uTorrent/3300\r\nAccept-Encoding: gzip\r\n";
                        client.PeerID = "-UT3300-%b9s" + GenerateIdString("random", 10, true, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&corrupt=0&key={key}{event}&numwant={numwant}&compact=1&no_peer_id=1";
                        client.DefNumWant = 200;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-UT3300-";
                        client.ProcessName = "uTorrent";
                        client.StartOffset = 0;
                        client.MaxOffset = 200000000;
                        break;
                    }
                case "uTorrent 3.2.0":
                    {
                        client.Name = "uTorrent 3.2.0";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = false;
                        client.Key = GenerateIdString("hex", 8, false, true);
                        client.Headers = "Host: {host}\r\nUser-Agent: uTorrent/3200\r\nAccept-Encoding: gzip\r\n";
                        client.PeerID = "-UT3200-z8\0." + GenerateIdString("random", 10, true, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&corrupt=0&key={key}{event}&numwant={numwant}&compact=1&no_peer_id=1";
                        client.DefNumWant = 200;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-UT3200-";
                        client.ProcessName = "uTorrent";
                        client.StartOffset = 0;
                        client.MaxOffset = 60000000;
                        break;
                    }
                case "uTorrent 2.0.1 (build 19078)":
                    {
                        client.Name = "uTorrent 2.0.1 (build 19078)";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = false;
                        client.Key = GenerateIdString("hex", 8, false, true);
                        client.Headers = "Host: {host}\r\nUser-Agent: uTorrent/2010(19078)\r\nAccept-Encoding: gzip\r\n";
                        client.PeerID = "-UT2010-%86J" + GenerateIdString("random", 10, true, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&corrupt=0&key={key}{event}&numwant={numwant}&compact=1&no_peer_id=1";
                        client.DefNumWant = 200;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-UT2010-";
                        client.ProcessName = "uTorrent";
                        client.StartOffset = 0;
                        client.MaxOffset = 60000000;
                        break;
                    }
                case "uTorrent 1.8.5 (build 17414)":
                    {
                        client.Name = "uTorrent 1.8.5 (build 17414)";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = false;
                        client.Key = GenerateIdString("hex", 8, false, true);
                        client.Headers = "Host: {host}\r\nUser-Agent: uTorrent/1850(17414)\r\nAccept-Encoding: gzip\r\n";
                        client.PeerID = "-UT1850-%06D" + GenerateIdString("random", 10, true, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&corrupt=0&key={key}{event}&numwant={numwant}&compact=1&no_peer_id=1";
                        client.DefNumWant = 200;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-UT1850-";
                        client.ProcessName = "uTorrent";
                        client.StartOffset = 0;
                        client.MaxOffset = 60000000;
                        break;
                    }
                case "uTorrent 1.8.1-beta(11903)":
                    {
                        client.Name = "uTorrent 1.8.1-beta(11903)";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = false;
                        client.Key = GenerateIdString("hex", 8, false, true);
                        client.Headers = "Host: {host}\r\nUser-Agent: uTorrent/181B(11903)\r\nAccept-Encoding: gzip\r\n";
                        client.PeerID = "-UT181B-%7f." + GenerateIdString("random", 10, true, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&corrupt=0&key={key}{event}&numwant={numwant}&compact=1&no_peer_id=1";
                        client.DefNumWant = 200;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-UT181B-";
                        client.ProcessName = "uTorrent";
                        client.StartOffset = 0;
                        client.MaxOffset = 60000000;
                        break;
                    }
                case "uTorrent 1.8.0":
                    {
                        client.Name = "uTorrent 1.8.0";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = false;
                        client.Key = GenerateIdString("hex", 8, false, true);
                        client.Headers = "Host: {host}\r\nUser-Agent: uTorrent/1800\r\nAccept-Encoding: gzip\r\n";
                        client.PeerID = "-UT1800-%25." + GenerateIdString("random", 10, true, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&corrupt=0&key={key}{event}&numwant={numwant}&compact=1&no_peer_id=1";
                        client.DefNumWant = 200;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-UT1800-";
                        client.ProcessName = "uTorrent";
                        client.StartOffset = 0;
                        client.MaxOffset = 60000000;
                        break;
                    }
                case "uTorrent 1.7.7":
                    {
                        client.Name = "uTorrent 1.7.7";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = false;
                        client.Key = GenerateIdString("hex", 8, false, true);
                        client.Headers = "Host: {host}\r\nUser-Agent: uTorrent/1770\r\nAccept-Encoding: gzip\r\n";
                        client.PeerID = "-UT1770-%f3%9f" + GenerateIdString("random", 10, true, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&key={key}{event}&numwant={numwant}&compact=1&no_peer_id=1";
                        client.DefNumWant = 200;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-UT1770-";
                        client.ProcessName = "uTorrent";
                        client.StartOffset = 0;
                        client.MaxOffset = 60000000;
                        break;
                    }
                case "uTorrent 1.7.6":
                    {
                        client.Name = "uTorrent 1.7.6";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = false;
                        client.Key = GenerateIdString("hex", 8, false, true);
                        client.Headers = "Host: {host}\r\nUser-Agent: uTorrent/1760\r\nAccept-Encoding: gzip\r\n";
                        client.PeerID = "-UT1760-%b3%9e" + GenerateIdString("random", 10, true, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&key={key}{event}&numwant={numwant}&compact=1&no_peer_id=1";
                        client.DefNumWant = 200;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-UT1760-";
                        client.ProcessName = "uTorrent";
                        client.StartOffset = 0;
                        client.MaxOffset = 60000000;
                        break;
                    }
                case "uTorrent 1.7.5":
                    {
                        client.Name = "uTorrent 1.7.5";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = false;
                        client.Key = GenerateIdString("hex", 8, false, true);
                        client.Headers = "Host: {host}\r\nUser-Agent: uTorrent/1750\r\nAccept-Encoding: gzip\r\n";
                        client.PeerID = "-UT1750-%fa%91" + GenerateIdString("random", 10, true, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&key={key}{event}&numwant={numwant}&compact=1&no_peer_id=1";
                        client.DefNumWant = 200;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-UT1750-";
                        client.ProcessName = "uTorrent";
                        client.StartOffset = 0;
                        client.MaxOffset = 60000000;
                        break;
                    }
                case "uTorrent 1.6.1":
                    {
                        client.Name = "uTorrent 1.6.1";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = false;
                        client.Key = GenerateIdString("hex", 8, false, true);
                        client.Headers = "Host: {host}\r\nUser-Agent: uTorrent/1610\r\nAccept-Encoding: gzip\r\n";
                        client.PeerID = "-UT1610-%ea%81" + GenerateIdString("random", 10, true, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&key={key}{event}&numwant={numwant}&compact=1&no_peer_id=1";
                        client.DefNumWant = 200;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-UT1610-";
                        client.ProcessName = "uTorrent";
                        client.StartOffset = 0;
                        client.MaxOffset = 60000000;
                        break;
                    }
                case "uTorrent 1.6":
                    {
                        client.Name = "uTorrent 1.6";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = false;
                        client.Key = GenerateIdString("hex", 8, false, true);
                        client.Headers = "Host: {host}\r\nUser-Agent: uTorrent/1600\r\nAccept-Encoding: gzip\r\n";
                        client.PeerID = "-UT1600-%d9%81" + GenerateIdString("random", 10, true, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&key={key}{event}&numwant={numwant}&compact=1&no_peer_id=1";
                        client.DefNumWant = 200;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-UT1600-";
                        client.ProcessName = "uTorrent";
                        client.StartOffset = 0;
                        client.MaxOffset = 60000000;
                        break;
                    }
                #endregion
                #region BitTorrent
                case "BitTorrent 6.0.3 (8642)":
                    {
                        client.Name = "BitTorrent 6.0.3 (8642)";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = false;
                        client.Key = GenerateIdString("hex", 8, false, true);
                        client.Headers = "Host: {host}\r\nUser-Agent: BitTorrent/6030\r\nAccept-Encoding: gzip\r\n";
                        client.PeerID = "M6-0-3--" + GenerateIdString("random", 12, true, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&key={key}{event}&numwant={numwant}&compact=1&no_peer_id=1";
                        client.DefNumWant = 200;
                        client.Parse = false;
                        client.SearchString = "";
                        client.ProcessName = "bittorrent";
                        client.StartOffset = 0;
                        client.MaxOffset = 100000000;
                        break;
                    }
                #endregion
                #region ABC
                case "ABC 3.1":
                    {
                        client.Name = "ABC 3.1";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = true;
                        client.Key = GenerateIdString("alphanumeric", 6, false, false);
                        client.Headers = "Host: {host}\r\nUser-Agent: ABC/ABC-3.1.0\r\nAccept-Encoding: gzip\r\n";
                        client.PeerID = "A310--" + GenerateIdString("alphanumeric", 14, false, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&trackerid=48&no_peer_id=1&compact=1{event}&key={key}";
                        client.Parse = true;
                        client.SearchString = "&peer_id=A310--";
                        client.ProcessName = "abc";
                        break;
                    }
                #endregion
                #region BitLord
                case "BitLord 1.1":
                    {
                        client.Name = "BitLord 1.1";
                        client.HttpProtocol = "HTTP/1.0";
                        client.HashUpperCase = true;
                        client.Key = GenerateIdString("numeric", 4, false, false);
                        client.Headers = "User-Agent: BitTorrent/3.4.2\r\nConnection: close\r\nAccept-Encoding: gzip, deflate\r\nHost: {host}\r\nCache-Control: no-cache\r\n";
                        client.PeerID = "exbc%01%01LORDCz%03%92" + GenerateIdString("random", 6, true, true);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&natmapped=1&uploaded={uploaded}&downloaded={downloaded}&left={left}&numwant=200&compact=1&no_peer_id=1&key={key}{event}";
                        break;
                    }
                #endregion
                #region BTuga
                case "BTuga 2.1.8":
                    {
                        client.Name = "BTuga 2.1.8";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = true;
                        client.Key = GenerateIdString("alphanumeric", 6, false, false);
                        client.Headers = "Host: {host}\r\nAccept-Encoding: gzip\r\nUser-Agent: BTuga/Revolution-2.6\r\n";
                        client.PeerID = "R26---" + GenerateIdString("alphanumeric", 14, false, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&no_peer_id=1&compact=1{event}&key={key}";
                        break;
                    }
                #endregion
                #region BitTornado
                case "BitTornado 0.3.17":
                    {
                        client.Name = "BitTornado 0.3.17";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = true;
                        client.Key = GenerateIdString("alphanumeric", 6, false, false);
                        client.Headers = "Host: {host}\r\nAccept-Encoding: gzip\r\nUser-Agent: BitTornado/T-0.3.17\r\n";
                        client.PeerID = "T03H-----" + GenerateIdString("alphanumeric", 11, false, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&no_peer_id=1&compact=1{event}&key={key}";
                        //client.Parse = true;
                        //client.SearchString = "&peer_id=T03H-----";
                        //client.ProcessName = "btdownloadgui";
                        break;
                    }
                #endregion
                #region Burst
                case "Burst 3.1.0b":
                    {
                        client.Name = "Burst 3.1.0b";
                        client.HttpProtocol = "HTTP/1.0";
                        client.HashUpperCase = true;
                        client.Key = GenerateIdString("hex", 8, false, false);
                        client.Headers = "Host: {host}\r\nAccept-Encoding: gzip\r\nUser-Agent: BitTorrent/brst1.1.3\r\n";
                        client.PeerID = "Mbrst1-1-3" + GenerateIdString("hex", 10, false, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&key={key}&uploaded={uploaded}&downloaded={downloaded}&left={left}&compact=1{event}";
                        break;
                    }
                #endregion
                #region BitTyrant
                case "BitTyrant 1.1":
                    {
                        client.Name = "BitTyrant 1.1";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = true;
                        client.Key = GenerateIdString("alphanumeric", 8, false, false);
                        client.Headers = "User-Agent: AzureusBitTyrant 2.5.0.0BitTyrant;Windows XP;Java 1.5.0_10\n\rConnection: close\n\rAccept-Encoding: gzip\n\rHost: {host}\n\rAccept: text/html, image/gif, image/jpeg, *; q=.2, */*; q=.2\n\rProxy-Connection: keep-alive\n\rContent-type: application/x-www-form-urlencoded\n\r";
                        client.PeerID = "AZ2500BT" + GenerateIdString("alphanumeric", 12, false, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&supportcrypto=1&port={port}&azudp={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}{event}&numwant={numwant}&no_peer_id=1&compact=1&key={key}";
                        client.DefNumWant = 50;
                        break;
                    }
                #endregion
                #region BitSpirit
                case "BitSpirit 3.6.0.200":
                    {
                        client.Name = "BitSpirit 3.6.0.200";
                        client.HttpProtocol = "HTTP/1.0";
                        client.HashUpperCase = false;
                        client.Key = GenerateIdString("hex", 8, false, true);
                        client.Headers = "User-Agent: BTSP/3602\r\nHost: {host}\r\nAccept_Encoding: gzip\r\nConnection: close";
                        client.PeerID = "%2dSP3602" + GenerateIdString("random", 13, true, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}{event}&key={key}&compact=1&numwant={numwant}&no_peer_id=1";
                        client.DefNumWant = 200;
                        break;
                    }
                case "BitSpirit 3.1.0.077":
                    {
                        client.Name = "BitSpirit 3.1.0.077";
                        client.HttpProtocol = "HTTP/1.0";
                        client.HashUpperCase = false;
                        client.Key = GenerateIdString("numeric", 3, false, false);
                        client.Headers = "User-Agent: BitTorrent/4.1.2\r\nHost: {host}\r\nAccept-Encoding: gzip\r\nConnection: close";
                        client.PeerID = "%00%03BS" + GenerateIdString("random", 16, true, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}{event}&key={key}&compact=1&numwant={numwant}";
                        client.DefNumWant = 200;
                        break;
                    }
                #endregion
                #region Deluge
                case "Deluge 1.2.0":
                    {
                        client.Name = "Deluge 1.2.0";
                        client.HttpProtocol = "HTTP/1.0";
                        client.HashUpperCase = false;
                        client.Key = GenerateIdString("alphanumeric", 8, false, false);
                        client.Headers = "Host: {host}\r\nUser-Agent: Deluge 1.2.0\r\nConnection: close\r\nAccept-Encoding: gzip\r\n";
                        client.PeerID = "-DE1200-" + GenerateIdString("alphanumeric", 12, false, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&event={event}&key={key}&compact=1&numwant={numwant}&supportcrypto=1&no_peer_id=1";
                        client.DefNumWant = 200;
                        client.Parse = false;
                        client.SearchString = "-DE1200-";
                        client.ProcessName = "deluge";
                        client.StartOffset = 0;
                        client.MaxOffset = 100000000;
                        break;
                    }
                case "Deluge 0.5.8.7":
                    {
                        client.Name = "Deluge 0.5.8.7";
                        client.HttpProtocol = "HTTP/1.0";
                        client.HashUpperCase = false;
                        client.Key = GenerateIdString("alphanumeric", 8, false, true);
                        client.Headers = "Host: {host}\r\nAccept-Encoding: gzip\r\nUser-Agent: Deluge 0.5.8.7\r\n";
                        client.PeerID = "-DE0587-" + GenerateIdString("alphanumeric", 12, false, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&event={event}&key={key}&compact=1&numwant={numwant}&supportcrypto=1&no_peer_id=1";
                        client.DefNumWant = 200;
                        client.Parse = false;
                        client.SearchString = "-DE0587-";
                        client.ProcessName = "deluge";
                        client.StartOffset = 0;
                        client.MaxOffset = 100000000;
                        break;
                    }
                case "Deluge 0.5.8.6":
                    {
                        client.Name = "Deluge 0.5.8.6";
                        client.HttpProtocol = "HTTP/1.0";
                        client.HashUpperCase = false;
                        client.Key = GenerateIdString("alphanumeric", 8, false, true);
                        client.Headers = "Host: {host}\r\nAccept-Encoding: gzip\r\nUser-Agent: Deluge 0.5.8.6\r\n";
                        client.PeerID = "-DE0586-" + GenerateIdString("alphanumeric", 12, false, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&event={event}&key={key}&compact=1&numwant={numwant}&supportcrypto=1&no_peer_id=1";
                        client.DefNumWant = 200;
                        client.Parse = false;
                        client.SearchString = "-DE0586-";
                        client.ProcessName = "deluge";
                        client.StartOffset = 0;
                        client.MaxOffset = 100000000;
                        break;
                    }
                #endregion
                #region KTorrent
                case "KTorrent 2.2.1":
                    {
                        client.Name = "KTorrent 2.2.1";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = false;
                        client.Key = GenerateIdString("numeric", 10, false, false);
                        client.Headers = "User-Agent: ktorrent/2.2.1\r\nAccept: text/html, image/gif, image/jpeg, *; q=.2, */*; q=.2\r\nAccept-Encoding: x-gzip, x-deflate, gzip, deflate\r\nHost: {host}\r\nConnection: Keep-Alive\n\r";
                        client.PeerID = "-KT2210-" + GenerateIdString("numeric", 12, false, false);
                        client.Query = "peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&compact=1&numwant={numwant}&key={key}{event}&info_hash={infohash}";
                        client.DefNumWant = 100;
                        break;
                    }
                #endregion
                #region Gnome BT
                case "Gnome BT 0.0.28-1":
                    {
                        client.Name = "Gnome BT 0.0.28-1";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = true;
                        client.Key = GenerateIdString("alphanumeric", 8, false, false);
                        client.Headers = "Host: {host}\r\nUser-Agent: Python-urllib/2.5\r\nConnection: close\r\nAccept-Encoding: gzip";
                        client.PeerID = "M3-4-2--" + GenerateIdString("alphanumeric", 12, false, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&key={key}&uploaded={uploaded}&downloaded={downloaded}&left={left}&compact=1{event}";
                        client.DefNumWant = 100;
                        break;
                    }
                #endregion
                default:
                    {
                        client.Name = "uTorrent 3.3.0";
                        client.HttpProtocol = "HTTP/1.1";
                        client.HashUpperCase = false;
                        client.Key = GenerateIdString("hex", 8, false, true);
                        client.Headers = "Host: {host}\r\nUser-Agent: uTorrent/3300\r\nAccept-Encoding: gzip\r\n";
                        client.PeerID = "-UT3300-z8\0." + GenerateIdString("random", 10, true, false);
                        client.Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&corrupt=0&key={key}{event}&numwant={numwant}&compact=1&no_peer_id=1";
                        client.DefNumWant = 200;
                        client.Parse = true;
                        client.SearchString = "&peer_id=-UT3300-";
                        client.ProcessName = "uTorrent";
                        client.StartOffset = 0;
                        client.MaxOffset = 200000000;
                        break;
                    }
            }
            return client;
        }
        internal string GenerateIdString(string keyType, int keyLength, bool urlencoding)
        {
            return GenerateIdString(keyType, keyLength, urlencoding, false);
        }
        internal string GenerateIdString(string keyType, int keyLength, bool urlencoding, bool upperCase)
        {
            string text1;
            string text2 = keyType;
            if (text2 != null)
            {
                if (text2 == "alphanumeric")
                {
                    text1 = stringGenerator.Generate(keyLength);
                    goto Label_00A2;
                }
                if (text2 == "numeric")
                {
                    text1 = stringGenerator.Generate(keyLength, "0123456789".ToCharArray());
                    goto Label_00A2;
                }
                if (text2 == "random")
                {
                    text1 = stringGenerator.Generate(keyLength, true);
                    goto Label_00A2;
                }
                if (text2 == "hex")
                {
                    text1 = stringGenerator.Generate(keyLength, "0123456789ABCDEF".ToCharArray());
                    goto Label_00A2;
                }
            }
            text1 = stringGenerator.Generate(keyLength);
        Label_00A2:
            if (urlencoding)
            {
                return stringGenerator.Generate(text1, upperCase);
            }
            if (upperCase)
            {
                text1 = text1.ToUpper();
            }
            return text1;
        }
        internal void cmbClient_SelectedIndexChanged(object sender, EventArgs e)
        {
            cmbVersion.Items.Clear();
            switch (cmbClient.SelectedItem.ToString())
            {
                case "BitComet":
                    {
                        cmbVersion.Items.Add("1.20");
                        cmbVersion.Items.Add("1.03");
                        cmbVersion.Items.Add("0.98");
                        cmbVersion.Items.Add("0.96");
                        cmbVersion.Items.Add("0.93");
                        cmbVersion.Items.Add("0.92");
                        cmbVersion.SelectedItem = "1.20";
                        if (customPeersNum.Text == "0" || customPeersNum.Text == "") customPeersNum.Text = "200";
                        break;
                    }
                case "Vuze":
                    {
                        cmbVersion.Items.Add("4.2.0.8");
                        cmbVersion.SelectedItem = "4.2.0.8";
                        if (customPeersNum.Text == "0" || customPeersNum.Text == "") customPeersNum.Text = "50";
                        break;
                    }
                case "Azureus":
                    {
                        cmbVersion.Items.Add("3.1.1.0");
                        cmbVersion.Items.Add("3.0.5.0");
                        cmbVersion.Items.Add("3.0.4.2");
                        cmbVersion.Items.Add("3.0.3.4");
                        cmbVersion.Items.Add("3.0.2.2");
                        cmbVersion.Items.Add("2.5.0.4");
                        cmbVersion.SelectedItem = "3.1.1.0";
                        if (customPeersNum.Text == "0" || customPeersNum.Text == "") customPeersNum.Text = "50";
                        break;
                    }
                case "uTorrent":
                    {
                        cmbVersion.Items.Add("3.3.0");
                        cmbVersion.Items.Add("3.2.0");
                        cmbVersion.Items.Add("2.0.1 (build 19078)");
                        cmbVersion.Items.Add("1.8.5 (build 17414)");
                        cmbVersion.Items.Add("1.8.1-beta(11903)");
                        cmbVersion.Items.Add("1.8.0");
                        cmbVersion.Items.Add("1.7.7");
                        cmbVersion.Items.Add("1.7.6");
                        cmbVersion.Items.Add("1.7.5");
                        cmbVersion.Items.Add("1.6.1");
                        cmbVersion.Items.Add("1.6");
                        cmbVersion.SelectedItem = "3.3.0";
                        if (customPeersNum.Text == "0" || customPeersNum.Text == "") customPeersNum.Text = "200";
                        break;
                    }
                case "BitTorrent":
                    {
                        cmbVersion.Items.Add("6.0.3 (8642)");
                        cmbVersion.SelectedItem = "6.0.3 (8642)";
                        if (customPeersNum.Text == "0" || customPeersNum.Text == "") customPeersNum.Text = "200";
                        break;
                    }
                case "BitLord":
                    {
                        cmbVersion.Items.Add("1.1");
                        cmbVersion.SelectedItem = "1.1";
                        if (customPeersNum.Text == "0" || customPeersNum.Text == "") customPeersNum.Text = "200";
                        break;
                    }
                case "ABC":
                    {
                        cmbVersion.Items.Add("3.1");
                        cmbVersion.SelectedItem = "3.1";
                        if (customPeersNum.Text == "0" || customPeersNum.Text == "") customPeersNum.Text = "200";
                        break;
                    }
                case "BTuga":
                    {
                        cmbVersion.Items.Add("2.1.8");
                        cmbVersion.SelectedItem = "2.1.8";
                        if (customPeersNum.Text == "0" || customPeersNum.Text == "") customPeersNum.Text = "200";
                        break;
                    }
                case "BitTornado":
                    {
                        cmbVersion.Items.Add("0.3.17");
                        cmbVersion.SelectedItem = "0.3.17";
                        if (customPeersNum.Text == "0" || customPeersNum.Text == "") customPeersNum.Text = "200";
                        break;
                    }
                case "Burst":
                    {
                        cmbVersion.Items.Add("3.1.0b");
                        cmbVersion.SelectedItem = "3.1.0b";
                        if (customPeersNum.Text == "0" || customPeersNum.Text == "") customPeersNum.Text = "200";
                        break;
                    }
                case "BitTyrant":
                    {
                        cmbVersion.Items.Add("1.1");
                        cmbVersion.SelectedItem = "1.1";
                        if (customPeersNum.Text == "0" || customPeersNum.Text == "") customPeersNum.Text = "50";
                        break;
                    }
                case "BitSpirit":
                    {
                        cmbVersion.Items.Add("3.6.0.200");
                        cmbVersion.Items.Add("3.1.0.077");
                        cmbVersion.SelectedItem = "3.6.0.200";
                        if (customPeersNum.Text == "0" || customPeersNum.Text == "") customPeersNum.Text = "200";
                        break;
                    }
                case "Deluge":
                    {
                        cmbVersion.Items.Add("1.2.0");
                        cmbVersion.Items.Add("0.5.8.7");
                        cmbVersion.Items.Add("0.5.8.6");
                        cmbVersion.SelectedItem = "1.2.0";
                        if (customPeersNum.Text == "0" || customPeersNum.Text == "") customPeersNum.Text = "200";
                        break;
                    }
                case "KTorrent":
                    {
                        cmbVersion.Items.Add("2.2.1");
                        cmbVersion.SelectedItem = "2.2.1";
                        if (customPeersNum.Text == "0" || customPeersNum.Text == "") customPeersNum.Text = "100";
                        break;
                    }
                case "Gnome BT":
                    {
                        cmbVersion.Items.Add("0.0.28-1");
                        cmbVersion.SelectedItem = "0.0.28-1";
                        if (customPeersNum.Text == "0" || customPeersNum.Text == "") customPeersNum.Text = "200";
                        break;
                    }
                default:
                    {
                        cmbClient.SelectedItem = DefaultClient;
                        if (customPeersNum.Text == "0" || customPeersNum.Text == "") customPeersNum.Text = "200";
                        break;
                    }
            }
            //getCurrentClient(GetClientName());
        }
        private void cmbVersion_SelectedValueChanged(object sender, EventArgs e)
        {
            if (getnew == false)
            {
                getnew = true;
                return;
            }
            if (chkNewValues.Checked)
            {
                SetCustomValues();
            }
        }
        #endregion
        #region Get(open) torrent
        internal void loadTorrentFileInfo(string torrentFilePath)
        {
            try
            {
                currentTorrentFile = new Torrent(torrentFilePath);
                torrentFile.Text = torrentFilePath;
                trackerAddress.Text = currentTorrentFile.Announce;
                shaHash.Text = ToHexString(currentTorrentFile.InfoHash);
                // text.Text = currentTorrentFile.totalLength.ToString();
                txtTorrentSize.Text = FormatFileSize((currentTorrentFile.totalLength));
            }
            catch (Exception ex)
            {
                AddLogLine(ex.ToString());
            }
        }
        private TorrentInfo getCurrentTorrent()
        {
            Uri trackerUri;
            TorrentInfo torrent = new TorrentInfo(0, 0);
            try
            {
                trackerUri = new Uri(trackerAddress.Text);
            }
            catch (Exception exception1)
            {
                AddLogLine(exception1.Message);
                return torrent;
            }
            torrent.tracker = trackerAddress.Text;
            torrent.trackerUri = trackerUri;
            torrent.hash = shaHash.Text;
            torrent.uploadRate = (Int64)(parseValidFloat(uploadRate.Text, 50) * 1024);
            //uploadRate.Text = (torrent.uploadRate / (float)1024).ToString();
            torrent.downloadRate = (Int64)(parseValidFloat(downloadRate.Text, 10) * 1024);
            //downloadRate.Text = (torrent.downloadRate / (float)1024).ToString();
            torrent.interval = ParseValidInt(interval.Text, torrent.interval);
            interval.Text = torrent.interval.ToString();
            double finishedPercent = ParseDouble(fileSize.Text, 0);
            if (finishedPercent < 0 || finishedPercent > 100)
            {
                AddLogLine("Finished value is invalid: " + fileSize.Text + ", assuming 0 as default value");
                finishedPercent = 0;
            }
            if (finishedPercent >= 100)
            {
                seedMode = true;
                finishedPercent = 100;
            }
            fileSize.Text = finishedPercent.ToString();
            long size = (long)currentTorrentFile.totalLength;
            if (currentTorrentFile != null)
            {
                if (finishedPercent == 0)
                {
                    torrent.totalsize = (long) currentTorrentFile.totalLength;
                }
                else if (finishedPercent == 100)
                {
                    torrent.totalsize = 0;
                }
                else
                {
                    torrent.totalsize = (long)((currentTorrentFile.totalLength * (100 - finishedPercent)) / 100);
                }
            }
            else
            {
                torrent.totalsize = 0;
            }
            torrent.left = torrent.totalsize;
            torrent.filename = torrentFile.Text;
            // deploy custom values
            torrent.port = getValueDefault(customPort.Text, torrent.port);
            customPort.Text = torrent.port;
            torrent.key = getValueDefault(customKey.Text, currentClient.Key);
            torrent.numberOfPeers = getValueDefault(customPeersNum.Text, torrent.numberOfPeers);
            currentClient.Key = getValueDefault(customKey.Text, currentClient.Key);
            torrent.peerID = getValueDefault(customPeerID.Text, currentClient.PeerID);
            currentClient.PeerID = getValueDefault(customPeerID.Text, currentClient.PeerID);
            // Add log info
            AddLogLine("TORRENT INFO:");
            AddLogLine("Torrent name: " + currentTorrentFile.Name);
            AddLogLine("Tracker address: " + torrent.tracker);
            AddLogLine("Hash code: " + torrent.hash);
            AddLogLine("Upload rate: " + torrent.uploadRate / 1024);
            AddLogLine("Download rate: " + torrent.downloadRate / 1024);
            AddLogLine("Update interval: " + torrent.interval);
            AddLogLine("Size: " + size / 1024);
            AddLogLine("Left: " + torrent.totalsize / 1024);
            AddLogLine("Finished: " + finishedPercent);
            AddLogLine("Filename: " + torrent.filename);
            AddLogLine("Number of peers: " + torrent.numberOfPeers);
            AddLogLine("Port: " + torrent.port);
            AddLogLine("Key: " + torrent.key);
            AddLogLine("PeerID: " + torrent.peerID + "\n" + "\n");
            return torrent;
        }
        internal void openFileDialog1_FileOk(object sender, CancelEventArgs e)
        {
            try
            {
                if (openFileDialog1.FileName == "") return;
                loadTorrentFileInfo(openFileDialog1.FileName);
                FileInfo file = new FileInfo(openFileDialog1.FileName);
                DefaultDirectory = file.DirectoryName;
            }
            catch { return; }
        }
        #endregion
        #region Buttons
        internal void closeButton_Click(object sender, EventArgs e)
        {
            ExitRatioMaster();
        }
        internal void StartButton_Click(object sender, EventArgs e)
        {
            if (!StartButton.Enabled) return;
            Seeders = -1;
            Leechers = -1;
            if (trackerAddress.Text == "" || shaHash.Text == "" || txtTorrentSize.Text == "")
            {
                MessageBox.Show("Please select valid torrent file!", "RatioMaster.NET " + version + " - ERROR");
                return;
            }
            // Check rem work
            if ((string)cmbStopAfter.SelectedItem == "After time:")
            {
                int res;
                bool bCheck = int.TryParse(txtStopValue.Text, out res);
                if (bCheck == false)
                {
                    MessageBox.Show("Please select valid number for Remaning Work\n\r- 0 - default - never stop\n\r- positive number (greater than 1000)", "RatioMaster.NET " + version + " - ERROR");
                    return;
                }
                else
                {
                    if (res < 1000 && res != 0)
                    {
                        MessageBox.Show("Please select valid number for Remaning Work\n\r- 0 - default - never stop\n\r- positive number (greater than 1000)", "RatioMaster.NET " + version + " - ERROR");
                        return;
                    }
                }
            }
            updateScrapStats("", "", "");
            totalRunningTimeCounter = 0;
            timerValue.Text = "updating...";
            //txtStopValue.Text = res.ToString();
            updateProcessStarted = true;
            seedMode = false;
            overHeadData = 0;
            requestScrap = checkRequestScrap.Checked;
            updateScrapStats("", "", "");
            StartButton.Enabled = false;
            StartButton.BackColor = SystemColors.Control;
            StopButton.Enabled = true;
            StopButton.BackColor = Color.Silver;
            manualUpdateButton.Enabled = true;
            manualUpdateButton.BackColor = Color.Silver;
            btnDefault.Enabled = false;
            interval.ReadOnly = true;
            fileSize.ReadOnly = true;
            cmbClient.Enabled = false;
            cmbVersion.Enabled = false;
            trackerAddress.ReadOnly = true;
            browseButton.Enabled = false;
            txtStopValue.Enabled = false;
            cmbStopAfter.Enabled = false;
            customPeersNum.Enabled = false;
            customPort.Enabled = false;
            currentClient = getCurrentClient(GetClientName());
            currentTorrent = getCurrentTorrent();
            currentProxy = getCurrentProxy();
            AddClientInfo();
            OpenTcpListener();
            Thread myThread = new Thread(startProcess);
            myThread.Name = "startProcess() Thread";
            myThread.Start();
            serverUpdateTimer.Start();
            remWork = 0;
            if ((string)cmbStopAfter.SelectedItem == "After time:") RemaningWork.Start();
            requestScrapeFromTracker(currentTorrent);
        }
        private void stopTimerAndCounters()
        {
            if (StartButton.InvokeRequired)
            {
                stopTimerAndCountersCallback callback1 = stopTimerAndCounters;
                Invoke(callback1, new object[0]);
            }
            else
            {
                Seeders = -1;
                Leechers = -1;
                totalRunningTimeCounter = 0;
                lblTotalTime.Text = "00:00";
                if (StartButton.Enabled) return;
                StartButton.Enabled = true;
                StopButton.Enabled = false;
                manualUpdateButton.Enabled = false;
                StartButton.BackColor = Color.Silver;
                StopButton.BackColor = SystemColors.Control;
                manualUpdateButton.BackColor = SystemColors.Control;
                btnDefault.Enabled = true;
                interval.ReadOnly = false;
                fileSize.ReadOnly = false;
                cmbClient.Enabled = true;
                cmbVersion.Enabled = true;
                trackerAddress.ReadOnly = false;
                browseButton.Enabled = true;
                txtStopValue.Enabled = true;
                cmbStopAfter.Enabled = true;
                customPeersNum.Enabled = true;
                customPort.Enabled = true;
                serverUpdateTimer.Stop();
                CloseTcpListener();
                temporaryIntervalCounter = 0;
                timerValue.Text = "stopped";
                currentTorrent.numberOfPeers = "0";
                updateProcessStarted = false;
                RemaningWork.Stop();
                remWork = 0;
            }
        }
        internal void StopButton_Click(object sender, EventArgs e)
        {
            if (!StopButton.Enabled) return;
            stopTimerAndCounters();
            Thread thread1 = new Thread(stopProcess);
            thread1.Name = "stopProcess() Thread";
            thread1.Start();
        }
        internal void manualUpdateButton_Click(object sender, EventArgs e)
        {
            if (!manualUpdateButton.Enabled) return;
            if (updateProcessStarted)
            {
                OpenTcpListener();
                temporaryIntervalCounter = currentTorrent.interval;
            }
        }
        internal void browseButton_Click(object sender, EventArgs e)
        {
            openFileDialog1.InitialDirectory = DefaultDirectory;
            openFileDialog1.ShowDialog();
        }
        internal void clearLogButton_Click(object sender, EventArgs e)
        {
            ClearLog();
        }
        internal void btnDefault_Click(object sender, EventArgs e)
        {
            getnew = false;                
            cmbClient.SelectedItem = DefaultClient;
            cmbVersion.SelectedItem = DefaultClientVersion;
            // custom
            chkNewValues.Checked = true;
            SetCustomValues();
            customPort.Text = "";
            customPeersNum.Text = "";
            // proxy
            comboProxyType.SelectedItem = "None";
            textProxyHost.Text = "";
            textProxyPass.Text = "";
            textProxyPort.Text = "";
            textProxyUser.Text = "";
            // check
            checkRequestScrap.Checked = true;
            checkTCPListen.Checked = true;
            // Options
            TorrentInfo torrent = new TorrentInfo(0, 0);
            int defup = (int)(torrent.uploadRate / 1024);
            int defd = (int)(torrent.downloadRate / 1024);
            uploadRate.Text = defup.ToString();
            downloadRate.Text = defd.ToString();
            fileSize.Text = "0";
            interval.Text = torrent.interval.ToString();
            // Log
            checkLogEnabled.Checked = true;
            // Random speeds
            chkRandUP.Checked = true;
            chkRandDown.Checked = true;
            txtRandUpMin.Text = "1";
            txtRandUpMax.Text = "10";
            txtRandDownMin.Text = "1";
            txtRandDownMax.Text = "10";
            // Random in next update
            checkRandomDownload.Checked = false;
            checkRandomUpload.Checked = false;
            RandomUploadFrom.Text = "10";
            RandomUploadTo.Text = "50";
            RandomDownloadFrom.Text = "10";
            RandomDownloadTo.Text = "100";
            // Other
            txtStopValue.Text = "0";
        }
        internal void btnSaveLog_Click(object sender, EventArgs e)
        {
            SaveLog.ShowDialog();
        }
        #endregion
        #region Parse Valid
        internal static double ParseDouble(string str, double defVal)
        {
            try
            {
                System.Globalization.NumberFormatInfo ni;
                System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InstalledUICulture;
                ni = (System.Globalization.NumberFormatInfo)ci.NumberFormat.Clone();
                ni.NumberDecimalSeparator = ",";
                str = str.Replace(".", ",");
                return double.Parse(str, ni);
            }
            catch { return defVal; }
        }
        internal static int ParseValidInt(string str, int defVal)
        {
            try
            { return int.Parse(str); }
            catch (Exception)
            { return defVal; }
        }
        internal static Int64 parseValidInt64(string str, Int64 defVal)
        {
            try
            { return Int64.Parse(str); }
            catch (Exception)
            { return defVal; }
        }
        static float parseValidFloat(string str, float defVal)
        {
            try
            { return float.Parse(str.Replace(".", ",")); }
            catch (Exception)
            { return defVal; }
        }
        internal static string getValueDefault(string value, string defValue)
        {
            if (value == "")
            { return defValue; }
            else
            { return value; }
        }
        #endregion
        #region Send Event To Tracker
        private bool haveInitialPeers;
        private bool sendEventToTracker(TorrentInfo torrentInfo, string eventType)
        {
            scrapStatsUpdated = false;
            currentTorrent = torrentInfo;
            string urlString = getUrlString(torrentInfo, eventType);
            ValueDictionary dictionary1;
            try
            {
                Uri uri = new Uri(urlString);
                TrackerResponse trackerResponse = MakeWebRequestEx(uri);
                if (trackerResponse != null && trackerResponse.Dict != null)
                {
                    dictionary1 = trackerResponse.Dict;
                    string failure = BEncode.String(dictionary1["failure reason"]);
                    if (failure.Length > 0)
                    {
                        AddLogLine("Tracker Error: " + failure);
                        if (!checkIgnoreFailureReason.Checked)
                        {
                            StopButton_Click(null, null);
                            AddLogLine("Stopped because of tracker error!!!");
                            return false;
                        }
                    }
                    else
                    {
                        foreach (string key in trackerResponse.Dict.Keys)
                        {
                            if (key != "failure reason" && key != "peers")
                            {
                                AddLogLine(key + ": " + BEncode.String(trackerResponse.Dict[key]));
                            }
                        }
                        if (dictionary1.Contains("interval"))
                        {
                            updateInterval(BEncode.String(dictionary1["interval"]));
                        }
                        if (dictionary1.Contains("complete") && dictionary1.Contains("incomplete"))
                        {
                            if (dictionary1.Contains("complete") && dictionary1.Contains("incomplete"))
                            {
                                updateScrapStats(BEncode.String(dictionary1["complete"]), BEncode.String(dictionary1["incomplete"]), "");
                                decimal leechers = ParseValidInt(BEncode.String(dictionary1["incomplete"]), 0);
                                if (leechers == 0)
                                {
                                    AddLogLine("Min number of leechers reached... setting upload speed to 0");
                                    updateTextBox(uploadRate, "0");
                                    chkRandUP.Checked = false;
                                }
                            }
                        }
                        if (dictionary1.Contains("peers"))
                        {
                            haveInitialPeers = true;
                            string text4;
                            if (dictionary1["peers"] is ValueString)
                            {
                                text4 = BEncode.String(dictionary1["peers"]);
                                Encoding encoding1 = Encoding.GetEncoding(0x6faf);
                                byte[] buffer1 = encoding1.GetBytes(text4);
                                BinaryReader reader1 = new BinaryReader(new MemoryStream(encoding1.GetBytes(text4)));
                                PeerList list1 = new PeerList();
                                for (int num1 = 0; num1 < buffer1.Length; num1 += 6)
                                {
                                    list1.Add(new Peer(reader1.ReadBytes(4), reader1.ReadInt16()));
                                }
                                reader1.Close();
                                AddLogLine("peers: " + list1);
                            }
                            else if (dictionary1["peers"] is ValueList)
                            {
                                //text4 = "";
                                ValueList list2 = (ValueList)dictionary1["peers"];
                                PeerList list3 = new PeerList();
                                foreach (object obj1 in list2)
                                {
                                    if (obj1 is ValueDictionary)
                                    {
                                        ValueDictionary dictionary2 = (ValueDictionary)obj1;
                                        list3.Add(new Peer(BEncode.String(dictionary2["ip"]), BEncode.String(dictionary2["port"]), BEncode.String(dictionary2["peer id"])));
                                    }
                                }
                                AddLogLine("peers: " + list3);
                            }
                            else
                            {
                                text4 = BEncode.String(dictionary1["peers"]);
                                AddLogLine("peers(x): " + text4);
                            }
                        }
                    }
                    return false;
                }
                else
                {
                    AddLogLine("No connection in sendEventToTracker() !!!");
                    return false;
                }
            }
            catch (Exception ex)
            {
                AddLogLine("Error in sendEventToTracker(): " + ex.Message);
                return false;
            }
        }
        private delegate void stopTimerAndCountersCallback();
        delegate void SetIntervalCallback(string param);
        internal void updateInterval(string param)
        {
            if (interval.InvokeRequired)
            {
                SetIntervalCallback del = updateInterval;
                Invoke(del, new object[] { param });
            }
            else
            {
                if (updateProcessStarted)
                {
                    int temp;
                    bool bParse = int.TryParse(param, out temp);
                    if (bParse)
                    {
                        if (temp > 3600) temp = 3600;
                        if (temp < 60) temp = 60;
                        currentTorrent.interval = temp;
                        AddLogLine("Updating Interval: " + temp);
                        interval.ReadOnly = false;
                        interval.Text = temp.ToString();
                        interval.ReadOnly = true;
                    }
                }
            }
        }
        private static long RoundByDenominator(long value, long denominator)
        {
            return (denominator * (value / denominator));
        }
        private string getUrlString(TorrentInfo torrentInfo, string eventType)
        {
            //Random random = new Random();
            string uploaded = "0";
            if (torrentInfo.uploaded > 0)
            {
                torrentInfo.uploaded = RoundByDenominator(torrentInfo.uploaded, 0x4000);
                uploaded = torrentInfo.uploaded.ToString();
                //uploaded = Convert.ToString(torrentInfo.uploaded + random.Next(1, 1023));
            }
            string downloaded = "0";
            if (torrentInfo.downloaded > 0)
            {
                torrentInfo.downloaded = RoundByDenominator(torrentInfo.downloaded, 0x10);
                downloaded = torrentInfo.downloaded.ToString();
                //downloaded = Convert.ToString(torrentInfo.downloaded + random.Next(1, 1023));
            }
            if (torrentInfo.left > 0)
            {
                torrentInfo.left = torrentInfo.totalsize - torrentInfo.downloaded;
            }
            string left = torrentInfo.left.ToString();
            string key = torrentInfo.key;
            string port = torrentInfo.port;
            string peerID = torrentInfo.peerID;
            string urlString;
            urlString = torrentInfo.tracker;
            if (urlString.Contains("?"))
            {
                urlString += "&";
            }
            else
            {
                urlString += "?";
            }
            if (eventType.Contains("started")) urlString = urlString.Replace("&natmapped=1&localip={localip}", "");
            if (!eventType.Contains("stopped")) urlString = urlString.Replace("&trackerid=48", "");
            urlString += currentClient.Query;
            urlString = urlString.Replace("{infohash}", HashUrlEncode(torrentInfo.hash, currentClient.HashUpperCase));
            urlString = urlString.Replace("{peerid}", peerID);
            urlString = urlString.Replace("{port}", port);
            urlString = urlString.Replace("{uploaded}", uploaded);
            urlString = urlString.Replace("{downloaded}", downloaded);
            urlString = urlString.Replace("{left}", left);
            urlString = urlString.Replace("{event}", eventType);
            if ((torrentInfo.numberOfPeers == "0") && !eventType.ToLower().Contains("stopped")) torrentInfo.numberOfPeers = "200";
            urlString = urlString.Replace("{numwant}", torrentInfo.numberOfPeers);
            urlString = urlString.Replace("{key}", key);
            urlString = urlString.Replace("{localip}", Functions.GetIp());
            return urlString;
        }
        #endregion
        #region Scrape
        private void requestScrapeFromTracker(TorrentInfo torrentInfo)
        {
            Seeders = -1;
            Leechers = -1;
            if (checkRequestScrap.Checked && !scrapStatsUpdated)
            {
                try
                {
                    string text1 = getScrapeUrlString(torrentInfo);
                    if (text1 == "")
                    {
                        AddLogLine("This tracker doesnt seem to support scrape");
                    }
                    Uri uri1 = new Uri(text1);
                    TrackerResponse response1 = MakeWebRequestEx(uri1);
                    if ((response1 != null) && (response1.Dict != null))
                    {
                        string text2 = BEncode.String(response1.Dict["failure reason"]);
                        if (text2.Length > 0)
                        {
                            AddLogLine("Tracker Error: " + text2);
                        }
                        else
                        {
                            AddLogLine("---------- Scrape Info -----------");
                            ValueDictionary dictionary1 = (ValueDictionary)response1.Dict["files"];
                            string text3 = Encoding.GetEncoding(0x4e4).GetString(currentTorrentFile.InfoHash);
                            if (dictionary1[text3].GetType() == typeof(ValueDictionary))
                            {
                                ValueDictionary dictionary2 = (ValueDictionary)dictionary1[text3];
                                AddLogLine("complete: " + BEncode.String(dictionary2["complete"]));
                                AddLogLine("downloaded: " + BEncode.String(dictionary2["downloaded"]));
                                AddLogLine("incomplete: " + BEncode.String(dictionary2["incomplete"]));
                                updateScrapStats(BEncode.String(dictionary2["complete"]), BEncode.String(dictionary2["incomplete"]), BEncode.String(dictionary2["downloaded"]));
                                decimal leechers = ParseValidInt(BEncode.String(dictionary2["incomplete"]), -1);
                                if (Leechers != -1  && (leechers == 0))
                                {
                                    AddLogLine("Min number of leechers reached... setting upload speed to 0");
                                    updateTextBox(uploadRate, "0");
                                    chkRandUP.Checked = false;
                                }
                            }
                            else
                            {
                                AddLogLine("Scrape returned : '" + ((ValueString)dictionary1[text3]).String + "'");
                            }
                        }
                    }
                }
                catch (Exception exception1)
                {
                    AddLogLine("Scrape Error: " + exception1.Message);
                }
            }
        }
        internal string getScrapeUrlString(TorrentInfo torrentInfo)
        {
            string urlString;
            urlString = torrentInfo.tracker;
            int index = urlString.LastIndexOf("/");
            if (urlString.Substring(index + 1, 8).ToLower() != "announce")
            {
                return "";
            }
            urlString = urlString.Substring(0, index + 1) + "scrape" + urlString.Substring(index + 9);
            string hash = HashUrlEncode(torrentInfo.hash, currentClient.HashUpperCase);
            if (urlString.Contains("?"))
            {
                urlString = urlString + "&";
            }
            else
            {
                urlString = urlString + "?";
            }
            return (urlString + "info_hash=" + hash);
        }
        #endregion
        #region Update Counters
        delegate void SetCountersCallback(TorrentInfo torrentInfo);
        private void updateCounters(TorrentInfo torrentInfo)
        {
            try
            {
                //Random random = new Random();
                // modify Upload Rate
                uploadCount.Text = FormatFileSize((ulong)torrentInfo.uploaded);
                Int64 uploadedR = torrentInfo.uploadRate + RandomSP(txtRandUpMin.Text, txtRandUpMax.Text, chkRandUP.Checked);
                // Int64 uploadedR = torrentInfo.uploadRate + (Int64)random.Next(10 * 1024) - 5 * 1024;
                if (uploadedR < 0) { uploadedR = 0; }
                torrentInfo.uploaded += uploadedR;
                // modify Download Rate
                downloadCount.Text = FormatFileSize((ulong)torrentInfo.downloaded);
                if (!seedMode && torrentInfo.downloadRate > 0)    // dont update download stats
                {
                    Int64 downloadedR = torrentInfo.downloadRate + RandomSP(txtRandDownMin.Text, txtRandDownMax.Text, chkRandDown.Checked);
                    // Int64 downloadedR = torrentInfo.downloadRate + (Int64)random.Next(10 * 1024) - 5 * 1024;
                    if (downloadedR < 0) { downloadedR = 0; }
                    torrentInfo.downloaded += downloadedR;
                    torrentInfo.left = torrentInfo.totalsize - torrentInfo.downloaded;
                }
                if (torrentInfo.left <= 0) // either seedMode or start seed mode
                {
                    torrentInfo.downloaded = torrentInfo.totalsize;
                    torrentInfo.left = 0;
                    torrentInfo.downloadRate = 0;
                    if (!seedMode)
                    {
                        currentTorrent = torrentInfo;
                        seedMode = true;
                        temporaryIntervalCounter = 0;
                        Thread myThread = new Thread(completedProcess);
                        myThread.Name = "completedProcess() Thread";
                        myThread.Start();
                    }
                }
                torrentInfo.interval = int.Parse(interval.Text);
                currentTorrent = torrentInfo;
                double finishedPercent;
                if (torrentInfo.totalsize == 0)
                {
                    fileSize.Text = "100";
                }
                else
                {
                    //finishedPercent = (((((float)currentTorrentFile.totalLength - (float)torrentInfo.totalsize) + (float)torrentInfo.downloaded) / (float)currentTorrentFile.totalLength) * 100);
                    finishedPercent = (((currentTorrentFile.totalLength - (float)torrentInfo.left)) / ((float)currentTorrentFile.totalLength)) * 100.0;
                    fileSize.Text = (finishedPercent >= 100) ? "100" : SetPrecision(finishedPercent.ToString(), 2);
                }
                downloadCount.Text = FormatFileSize((ulong)torrentInfo.downloaded);
                // modify Ratio Lable
                if (torrentInfo.downloaded / 1024 < 100)
                {
                    lblTorrentRatio.Text = "NaN";
                }
                else
                {
                    float data = torrentInfo.uploaded / (float)torrentInfo.downloaded;
                    lblTorrentRatio.Text = SetPrecision(data.ToString(), 2);
                }
            }
            catch (Exception e)
            {
                AddLogLine(e.Message);
                SetCountersCallback d = updateCounters;
                Invoke(d, new object[] { torrentInfo });
            }
        }
        private static string SetPrecision(string data, int prec)
        {
            float pow = (float)Math.Pow(10, prec);
            float wdata = float.Parse(data);
            wdata = wdata * pow;
            int curr = (int)wdata;
            wdata = curr / pow;
            return wdata.ToString();
        }
        int Seeders = -1;
        int Leechers = -1;
        internal void updateScrapStats(string seedStr, string leechStr, string finishedStr)
        {
            try
            {
                Seeders = int.Parse(seedStr);
                Leechers = int.Parse(leechStr);
            }
            catch(Exception)
            {
                Seeders = -1;
                Leechers = -1;
            }
            //if (seedLabel.InvokeRequired)
            //{
            //    updateScrapCallback d = new updateScrapCallback(updateScrapStats);
            //    Invoke(d, new object[] { seedStr, leechStr, finishedStr });
            //}
            //else
            //{
                seedLabel.Text = "Seeders: " + seedStr;
                leechLabel.Text = "Leechers: " + leechStr;
                scrapStatsUpdated = true;
                //AddLogLine("Scrap Stats Updated" + "\n" + "\n");
            //}
        }
        internal void StopModule()
        {
            try
            {
                if ((string)cmbStopAfter.SelectedItem == "When seeders <")
                {
                    if (Seeders > -1 && Seeders < int.Parse(txtStopValue.Text)) StopButton_Click(null, null);
                }
                if ((string)cmbStopAfter.SelectedItem == "When leechers <")
                {
                    if (Leechers > -1 && Leechers < int.Parse(txtStopValue.Text)) StopButton_Click(null, null);
                }
                if ((string)cmbStopAfter.SelectedItem == "When uploaded >")
                {
                    if (currentTorrent.uploaded > int.Parse(txtStopValue.Text) * 1024 * 1024) StopButton_Click(null, null);
                }
                if ((string)cmbStopAfter.SelectedItem == "When downloaded >")
                {
                    if (currentTorrent.downloaded > int.Parse(txtStopValue.Text) * 1024 * 1024) StopButton_Click(null, null);
                }
                if ((string)cmbStopAfter.SelectedItem == "When leechers/seeders <")
                {
                    if ((Leechers / (double)Seeders) < double.Parse(txtStopValue.Text)) StopButton_Click(null, null);
                }
            }
            catch (Exception ex)
            {
                AddLogLine("Error in stopping module!!!: " + ex.Message);
                return;
            }
        }
        internal int totalRunningTimeCounter;
        internal void serverUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (updateProcessStarted)
            {
                if (haveInitialPeers)
                {
                    updateCounters(currentTorrent);
                }
                int num1 = currentTorrent.interval - temporaryIntervalCounter;
                totalRunningTimeCounter++;
                lblTotalTime.Text = ConvertToTime(totalRunningTimeCounter);
                StopModule();
                if (num1 > 0)
                {
                    temporaryIntervalCounter++;
                    timerValue.Text = ConvertToTime(num1);
                }
                else
                {
                    randomiseSpeeds();
                    OpenTcpListener();
                    Thread thread1 = new Thread(continueProcess);
                    temporaryIntervalCounter = 0;
                    timerValue.Text = "0";
                    thread1.Name = "continueProcess() Thread";
                    thread1.Start();
                }
            }
        }
        internal void randomiseSpeeds()
        {
            try
            {
                if (checkRandomUpload.Checked)
                {
                    uploadRate.Text = (RandomSP(RandomUploadFrom.Text, RandomUploadTo.Text, true)/1024).ToString();
                    //uploadRate.Text = ((int)random1.Next(int.Parse(RandomUploadFrom.Text), int.Parse(RandomUploadTo.Text)) + (int)single1).ToString();
                }
                if (checkRandomDownload.Checked)
                {
                    downloadRate.Text = (RandomSP(RandomDownloadFrom.Text, RandomDownloadTo.Text, true)/1024).ToString();
                    // downloadRate.Text = ((int)random1.Next(int.Parse(RandomDownloadFrom.Text), int.Parse(RandomDownloadTo.Text)) + (int)single2).ToString();
                }
            }
            catch (Exception exception1)
            {
                AddLogLine("Failed to randomise upload/download speeds: " + exception1.Message);
            }
        }
        internal int RandomSP(string min, string max, bool ret)
        {
            if (ret == false) return rand.Next(10);
            int minn = int.Parse(min);
            int maxx = int.Parse(max);
            int rett = rand.Next(GetMin(minn, maxx), GetMax(minn, maxx)) * 1024;
            return rett;
        }
        internal static int GetMin(int p1, int p2)
        {
            if (p1 < p2) return p1;
            else return p2;
        }
        internal static int GetMax(int p1, int p2)
        {
            if (p1 > p2) return p1;
            else return p2;
        }
        #endregion
        #region Help functions
        private delegate void updateTextBoxCallback(TextBox textbox, string text);
        internal void updateTextBox(TextBox textbox, string text)
        {
            if (textbox.InvokeRequired)
            {
                updateTextBoxCallback callback1 = updateTextBox;
                Invoke(callback1, new object[] { textbox, text });
            }
            else
            {
                textbox.Text = text;
            }
        }
        private delegate void updateLabelCallback(Label textbox, string text);
        private void updateTextBox(Label textbox, string text)
        {
            if (textbox.InvokeRequired)
            {
                updateLabelCallback callback1 = updateTextBox;
                Invoke(callback1, new object[] { textbox, text });
            }
            else
            {
                textbox.Text = text;
            }
        }
        internal static string FormatFileSize(ulong fileSize)
        {
            if (fileSize < 0)
            {
                throw new ArgumentOutOfRangeException("fileSize");
            }
            if (fileSize >= 0x40000000)
            {
                return string.Format("{0:########0.00} GB", ((double)fileSize) / 1073741824);
            }
            if (fileSize >= 0x100000)
            {
                return string.Format("{0:####0.00} MB", ((double)fileSize) / 1048576);
            }
            if (fileSize >= 0x400)
            {
                return string.Format("{0:####0.00} KB", ((double)fileSize) / 1024);
            }
            return string.Format("{0} bytes", fileSize);
        }
        internal static string ToHexString(byte[] bytes)
        {
            char[] hexDigits = {
                    '0', '1', '2', '3', '4', '5', '6', '7',
                    '8', '9', 'A', 'B', 'C', 'D', 'E', 'F'
                    };

            char[] chars = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                int b = bytes[i];
                chars[i * 2] = hexDigits[b >> 4];
                chars[i * 2 + 1] = hexDigits[b & 0xF];
            }
            return new string(chars);
        }
        internal static string ConvertToTime(int seconds)
        {
            string ret;
            if (seconds < 60 * 60)
            {
                ret = (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
            }
            else
            {
                ret = (seconds / (60 * 60)).ToString("00") + ":" + ((seconds % (60 * 60)) / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
            }
            return ret;
        }
        internal string HashUrlEncode(string decoded, bool upperCase)
        {
            StringBuilder ret = new StringBuilder();
            RandomStringGenerator stringGen = new RandomStringGenerator();
            try
            {
                for (int i = 0; i < decoded.Length; i = i + 2)
                {
                    char tempChar;
                    // the only case in which something should not be escaped, is when it is alphanum,
                    // or it's in marks
                    // in all other cases, encode it.
                    tempChar = (char)Convert.ToUInt16(decoded.Substring(i, 2), 16);
                    ret.Append(tempChar);
                }
            }
            catch (Exception ex)
            {
                AddLogLine(ex.ToString());
            }
            return stringGen.Generate(ret.ToString(), upperCase);
        }
        #endregion
        internal SocketEx createSocket()
        {
            // create SocketEx object according to proxy settings
            SocketEx sock = null;
            try
            {
                sock = new SocketEx(currentProxy.proxyType, currentProxy.proxyServer, currentProxy.proxyPort, currentProxy.proxyUser, currentProxy.proxyPassword);
                sock.SetTimeout(0x30d40);
            }
            catch (Exception sockError)
            {
                AddLogLine("createSocket error: " + sockError.Message);
            }
            return sock;
        }
        private ProxyInfo getCurrentProxy()
        {
            Encoding _usedEnc = Encoding.GetEncoding(0x4e4);
            ProxyInfo curProxy = new ProxyInfo();
            switch (comboProxyType.SelectedIndex)
            {
                case 0:
                    curProxy.proxyType = ProxyType.None;
                    break;
                case 1:
                    curProxy.proxyType = ProxyType.HttpConnect;
                    break;
                case 2:
                    curProxy.proxyType = ProxyType.Socks4;
                    break;
                case 3:
                    curProxy.proxyType = ProxyType.Socks4a;
                    break;
                case 4:
                    curProxy.proxyType = ProxyType.Socks5;
                    break;
                default:
                    curProxy.proxyType = ProxyType.None;
                    break;
            }
            curProxy.proxyServer = textProxyHost.Text;
            curProxy.proxyPort = ParseValidInt(textProxyPort.Text, 0);
            curProxy.proxyUser = _usedEnc.GetBytes(textProxyUser.Text);
            curProxy.proxyPassword = _usedEnc.GetBytes(textProxyPass.Text);
            // Add log info
            Encoding enc = System.Text.Encoding.ASCII;
            AddLogLine("PROXY INFO:");
            AddLogLine("proxyType = " + curProxy.proxyType);
            AddLogLine("proxyServer = " + curProxy.proxyServer);
            AddLogLine("proxyPort = " + curProxy.proxyPort);
            AddLogLine("proxyUser = " + enc.GetString(curProxy.proxyUser));
            AddLogLine("proxyPassword = " + enc.GetString(curProxy.proxyPassword) + "\n" + "\n");
            return curProxy;
        }
        private TrackerResponse MakeWebRequestEx(Uri reqUri)
        {
            Encoding _usedEnc = Encoding.GetEncoding(0x4e4);
            SocketEx sock = null;
            TrackerResponse trackerResponse;
            try
            {
                string host = reqUri.Host;
                int port = reqUri.Port;
                string path = reqUri.PathAndQuery;
                AddLogLine("Connecting to tracker (" + host + ") in port " + port);
                sock = createSocket();
                sock.PreAuthenticate = false;
                int num2 = 0;
                bool flag1 = false;
                while ((num2 < 5) && !flag1)
                {
                    try
                    {
                        sock.Connect(host, port);
                        flag1 = true;
                        AddLogLine("Connected Successfully");
                        continue;
                    }
                    catch (Exception exception1)
                    {
                        AddLogLine("Exception: " + exception1.Message + "; Type: " + exception1.GetType());
                        AddLogLine("Failed connection attempt: " + num2);
                        num2++;
                        continue;
                    }
                }
                string cmd = "GET " + path + " " + currentClient.HttpProtocol + "\r\n" + currentClient.Headers.Replace("{host}", host) + "\r\n";
                AddLogLine("======== Sending Command to Tracker ========");
                AddLogLine(cmd);
                sock.Send(_usedEnc.GetBytes(cmd));
                //simple reading loop
                //read while have the data
                try
                {
                    byte[] data = new byte[32 * 1024];
                    MemoryStream memStream = new MemoryStream();
                    while (true)
                    {
                        int dataLen = sock.Receive(data);
                        if (0 == dataLen)
                            break;
                        memStream.Write(data, 0, dataLen);
                    }
                    if (memStream.Length == 0)
                    {
                        AddLogLine("Error : Tracker Response is empty");
                        return null;
                    }
                    trackerResponse = new TrackerResponse(memStream);
                    if (trackerResponse.doRedirect)
                    {
                        return MakeWebRequestEx(new Uri(trackerResponse.RedirectionURL));
                    }
                    AddLogLine("======== Tracker Response ========");
                    AddLogLine(trackerResponse.Headers);
                    if (trackerResponse.Dict == null)
                    {
                        AddLogLine("*** Failed to decode tracker response :");
                        AddLogLine(trackerResponse.Body);
                    }
                    memStream.Dispose();
                    return trackerResponse;
                }
                catch (Exception ex)
                {
                    sock.Close();
                    AddLogLine(Environment.NewLine + ex.Message);
                    return null;
                }
            }
            catch (Exception ex)
            {
                if (null != sock) sock.Close();
                AddLogLine("Exception:" + ex.Message);
                return null;
            }
            //if (null != sock) sock.Close();
            //else return null;
        }
        internal void RemaningWork_Tick(object sender, EventArgs e)
        {
            if (txtStopValue.Text == "0")
            {
                return;
            }
            else
            {
                remWork++;
                int RW = int.Parse(txtStopValue.Text);
                int diff = RW - remWork;
                txtRemTime.Text = ConvertToTime(diff);
                if (remWork >= RW)
                {
                    txtRemTime.Text = "0";
                    RemaningWork.Stop();
                    StopButton_Click(null, null);
                }
            }
        }
        #region Process
        internal void stopProcess()
        {
            sendEventToTracker(currentTorrent, "&event=stopped");
        }
        internal void completedProcess()
        {
            sendEventToTracker(currentTorrent, "&event=completed");
            requestScrapeFromTracker(currentTorrent);
        }
        internal void continueProcess()
        {
            sendEventToTracker(currentTorrent, "");
            requestScrapeFromTracker(currentTorrent);
        }
        internal void startProcess()
        {
            if (sendEventToTracker(currentTorrent, "&event=started"))
            {
                updateProcessStarted = true;
                requestScrapeFromTracker(currentTorrent);
            }
        }
        #endregion
        #region Change Speeds
        internal void uploadRate_TextChanged(object sender, EventArgs e)
        {
            if (uploadRate.Text == "")
            {
                currentTorrent.uploadRate = 0;
            }
            else
            {
                TorrentInfo torrent = new TorrentInfo(0, 0);
                currentTorrent.uploadRate = parseValidInt64(uploadRate.Text, (torrent.uploadRate / 1024)) * 1024;
            }
            AddLogLine("Upload rate changed to " + (currentTorrent.uploadRate / 1024));
        }
        internal void downloadRate_TextChanged(object sender, EventArgs e)
        {
            if (downloadRate.Text == "")
            {
                currentTorrent.downloadRate = 0;
            }
            else
            {
                TorrentInfo torrent = new TorrentInfo(0, 0);
                currentTorrent.downloadRate = parseValidInt64(downloadRate.Text, (torrent.downloadRate / 1024)) * 1024;
            }
            AddLogLine("Download rate changed to " + (currentTorrent.downloadRate / 1024));
        }
        #endregion
        #region Settings
        internal void ReadSettings()
        {
            try
            {
                RegistryKey reg = Registry.CurrentUser.OpenSubKey("Software\\RatioMaster.NET", true);
                //TorrentInfo torrent = new TorrentInfo(0, 0);
                if (reg == null)
                {
                    // The key doesn't exist; create it / open it
                    Registry.CurrentUser.CreateSubKey("Software\\RatioMaster.NET");
                    return;
                }
                string Version = (string)reg.GetValue("Version", "none");
                if (Version == "none")
                {
                    btnDefault_Click(null, null);
                    return;
                }
                chkNewValues.Checked = ItoB((int)reg.GetValue("NewValues", true));
                getnew = false;
                cmbClient.SelectedItem = reg.GetValue("Client", DefaultClient);
                getnew = false;
                cmbVersion.SelectedItem = reg.GetValue("ClientVersion", DefaultClientVersion);
                uploadRate.Text = ((string)reg.GetValue("UploadRate", uploadRate.Text));
                downloadRate.Text = ((string)reg.GetValue("DownloadRate", downloadRate.Text));
                fileSize.Text = (string)reg.GetValue("fileSize", "0");
                //fileSize.Text = "0";
                interval.Text = (reg.GetValue("Interval", interval.Text)).ToString();
                DefaultDirectory = (string)reg.GetValue("Directory", DefaultDirectory);
                checkTCPListen.Checked = ItoB((int)reg.GetValue("TCPlistener", BtoI(checkTCPListen.Checked)));
                checkRequestScrap.Checked = ItoB((int)reg.GetValue("ScrapeInfo", BtoI(checkRequestScrap.Checked)));
                checkLogEnabled.Checked = ItoB((int)reg.GetValue("EnableLog", BtoI(checkLogEnabled.Checked)));
                // Radnom value
                chkRandUP.Checked = ItoB((int)reg.GetValue("GetRandUp", BtoI(chkRandUP.Checked)));
                chkRandDown.Checked = ItoB((int)reg.GetValue("GetRandDown", BtoI(chkRandDown.Checked)));
                txtRandUpMin.Text = (string)reg.GetValue("MinRandUp", txtRandUpMin.Text);
                txtRandUpMax.Text = (string)reg.GetValue("MaxRandUp", txtRandUpMax.Text);
                txtRandDownMin.Text = (string)reg.GetValue("MinRandDown", txtRandDownMin.Text);
                txtRandDownMax.Text = (string)reg.GetValue("MaxRandDown", txtRandDownMax.Text);
                // Custom values
                if (chkNewValues.Checked == false)
                {
                    customKey.Text = (string)reg.GetValue("CustomKey", customKey.Text);
                    customPeerID.Text = (string)reg.GetValue("CustomPeerID", customPeerID.Text);
                    lblGenStatus.Text = "Generation status: " + "using last saved values";
                }
                else
                {
                    SetCustomValues();
                }
                customPort.Text = (string)reg.GetValue("CustomPort", customPort.Text);
                customPeersNum.Text = (string)reg.GetValue("CustomPeers", customPeersNum.Text);
                // Radnom value on next
                checkRandomUpload.Checked = ItoB((int)reg.GetValue("GetRandUpNext", BtoI(checkRandomUpload.Checked)));
                checkRandomDownload.Checked = ItoB((int)reg.GetValue("GetRandDownNext", BtoI(checkRandomDownload.Checked)));
                RandomUploadFrom.Text = (string)reg.GetValue("MinRandUpNext", RandomUploadFrom.Text);
                RandomUploadTo.Text = (string)reg.GetValue("MaxRandUpNext", RandomUploadTo.Text);
                RandomDownloadFrom.Text = (string)reg.GetValue("MinRandDownNext", RandomDownloadFrom.Text);
                RandomDownloadTo.Text = (string)reg.GetValue("MaxRandDownNext", RandomDownloadTo.Text);
                // Stop after...
                cmbStopAfter.SelectedItem = reg.GetValue("StopWhen", "Never");
                txtStopValue.Text = (string)reg.GetValue("StopAfter", txtStopValue.Text);
                // Proxy
                comboProxyType.SelectedItem = reg.GetValue("ProxyType", comboProxyType.SelectedItem);
                textProxyHost.Text = (string)reg.GetValue("ProxyAdress", textProxyHost.Text);
                textProxyUser.Text = (string)reg.GetValue("ProxyUser", textProxyUser.Text);
                textProxyPass.Text = (string)reg.GetValue("ProxyPass", textProxyPass.Text);
                textProxyPort.Text = (string)reg.GetValue("ProxyPort", textProxyPort.Text);
                checkIgnoreFailureReason.Checked = ItoB((int)reg.GetValue("IgnoreFailureReason", BtoI(checkIgnoreFailureReason.Checked)));
            }
            catch (Exception e)
            {
                AddLogLine("Error in ReadSettings(): " + e.Message);
            }
        }
        internal static int BtoI(bool b)
        {
            if (b) return 1;
            else return 0;
        }
        internal static bool ItoB(int param)
        {
            if (param == 0) return false;
            if (param == 1) return true;
            return true;
        }
        #endregion
        #region Custom values
        internal void chkNewValues_CheckedChanged(object sender, EventArgs e)
        {
            if (chkNewValues.Checked)
            {
                SetCustomValues();
            }
        }
        internal void GetRandCustVal()
        {
            string clientname = GetClientName();
            currentClient = getCurrentClient(clientname);
            customKey.Text = currentClient.Key;
            customPeerID.Text = currentClient.PeerID;
            currentTorrent.port = rand.Next(1025, 65535).ToString();
            customPort.Text = currentTorrent.port;
            currentTorrent.numberOfPeers = currentClient.DefNumWant.ToString();
            customPeersNum.Text = currentTorrent.numberOfPeers;
            lblGenStatus.Text = "Generation status: " + "generated new values for " + clientname;
        }
        internal void SetCustomValues()
        {
            string clientname = GetClientName();
            currentClient = getCurrentClient(clientname);
            AddLogLine("Client changed: " + clientname);
            if (!currentClient.Parse) GetRandCustVal();
            else
            {
                string searchstring = currentClient.SearchString;
                long maxoffset = currentClient.MaxOffset;
                long startoffset = currentClient.StartOffset;
                string process = currentClient.ProcessName;
                string pversion = cmbVersion.SelectedItem.ToString();
                if (GETDATA(process, pversion, searchstring, startoffset, maxoffset))
                {
                    customKey.Text = currentClient.Key;
                    customPeerID.Text = currentClient.PeerID;
                    customPort.Text = currentTorrent.port;
                    customPeersNum.Text = currentTorrent.numberOfPeers;
                    lblGenStatus.Text = "Generation status: " + clientname + " found! Parsed all values!";
                }
                else
                {
                    GetRandCustVal();
                }
            }
        }
        internal bool GETDATA(string client, string pversion, string SearchString, long startoffset, long maxoffset)
        {
            try
            {
                ProcessMemoryReader pReader;
                long absoluteEndOffset = maxoffset;
                long absoluteStartOffset = startoffset;
                string clientSearchString = SearchString;
                uint bufferSize = 0x10000;
                string currentClientProcessName = client.ToLower();
                long currentOffset;
                Encoding enc = Encoding.ASCII;
                Process process1 = FindProcessByName(currentClientProcessName);
                if (process1 == null)
                {
                    return false;
                }
                currentOffset = absoluteStartOffset;
                pReader = new ProcessMemoryReader();
                pReader.ReadProcess = process1;
                bool flag1 = false;
                //AddLogLine("Debug: before pReader.OpenProcess();");
                pReader.OpenProcess();
                //AddLogLine("Debug: pReader.OpenProcess();");
                while (currentOffset < absoluteEndOffset)
                {
                    long num2;
                    //AddLogLine("Debug: " + currentOffset.ToString());
                    int num1;
                    byte[] buffer1 = pReader.ReadProcessMemory((IntPtr)currentOffset, bufferSize, out num1);
                    //pReader.saveArrayToFile(buffer1, @"D:\Projects\NRPG Ratio\NRPG RatioMaster MULTIPLE\RatioMaster source\bin\Release\tests\test" + currentOffset.ToString() + ".txt");
                    num2 = getStringOffsetInsideArray(buffer1, enc, clientSearchString);
                    if (num2 >= 0)
                    {
                        flag1 = true;
                        string text1 = enc.GetString(buffer1);
                        Match match1 = new Regex("&peer_id=(.+?)(&| )", RegexOptions.Compiled).Match(text1);
                        if (match1.Success)
                        {
                            currentClient.PeerID = match1.Groups[1].ToString();
                            AddLogLine("====> PeerID = " + currentClient.PeerID);
                        }
                        match1 = new Regex("&key=(.+?)(&| )", RegexOptions.Compiled).Match(text1);
                        if (match1.Success)
                        {
                            currentClient.Key = match1.Groups[1].ToString();
                            AddLogLine("====> Key = " + currentClient.Key);
                        }
                        match1 = new Regex("&port=(.+?)(&| )", RegexOptions.Compiled).Match(text1);
                        if (match1.Success)
                        {
                            currentTorrent.port = match1.Groups[1].ToString();
                            AddLogLine("====> Port = " + currentTorrent.port);
                        }
                        match1 = new Regex("&numwant=(.+?)(&| )", RegexOptions.Compiled).Match(text1);
                        if (match1.Success)
                        {
                            currentTorrent.numberOfPeers = match1.Groups[1].ToString();
                            AddLogLine("====> NumWant = " + currentTorrent.numberOfPeers);
                            int res;
                            if (!int.TryParse(currentTorrent.numberOfPeers, out res)) currentTorrent.numberOfPeers = currentClient.DefNumWant.ToString();
                        }
                        num2 += currentOffset;
                        AddLogLine("currentOffset = " + currentOffset);
                        break;
                    }
                    currentOffset += (int)bufferSize;
                }
                pReader.CloseHandle();
                if (flag1)
                {
                    AddLogLine("Search finished successfully!");
                    return true;
                }
                else
                {
                    AddLogLine("Search failed. Make sure that torrent client {" + GetClientName() + "} is running and that at least one torrent is working.");
                    return false;
                }
            }
            catch(Exception ex)
            {
                AddLogLine("Error when parsing: " + ex.Message);
                return false;
            }
        }
        private Process FindProcessByName(string processName)
        {
            AddLogLine("Looking for " + processName + " process...");
            Process[] processArray1 = Process.GetProcessesByName(processName);
            if (processArray1.Length == 0)
            {
                string text1 = "No " + processName + " process found. Make sure that torrent client is running.";
                AddLogLine(text1);
                return null;
            }
            AddLogLine(processName + " process found! ");
            return processArray1[0];
        }
        private static int getStringOffsetInsideArray(byte[] memory, Encoding enc, string clientSearchString)
        {
            return enc.GetString(memory).IndexOf(clientSearchString);
        }
        #endregion
        private void cmbStopAfter_SelectedIndexChanged(object sender, EventArgs e)
        {
            if ((string)cmbStopAfter.SelectedItem == "Never")
            {
                lblStopAfter.Text = "";
                txtStopValue.Text = "";
                txtStopValue.Visible = false;
            }
            if ((string)cmbStopAfter.SelectedItem == "After time:")
            {
                lblStopAfter.Text = "seconds";
                txtStopValue.Text = "3600";
                txtStopValue.Visible = true;
            }
            if ((string)cmbStopAfter.SelectedItem == "When seeders <")
            {
                lblStopAfter.Text = "";
                txtStopValue.Text = "10";
                txtStopValue.Visible = true;
            }
            if ((string)cmbStopAfter.SelectedItem == "When leechers <")
            {
                lblStopAfter.Text = "";
                txtStopValue.Text = "10";
                txtStopValue.Visible = true;
            }
            if ((string)cmbStopAfter.SelectedItem == "When uploaded >")
            {
                lblStopAfter.Text = "Mb";
                txtStopValue.Text = "1024";
                txtStopValue.Visible = true;
            }
            if ((string)cmbStopAfter.SelectedItem == "When downloaded >")
            {
                lblStopAfter.Text = "Mb";
                txtStopValue.Text = "1024";
                txtStopValue.Visible = true;
            }
            if ((string)cmbStopAfter.SelectedItem == "When leechers/seeders <")
            {
                lblStopAfter.Text = "";
                txtStopValue.Text = "1,000";
                txtStopValue.Visible = true;
            }
        }
        #endregion
        public override string ToString()
        {
            return "RatioMaster";
        }
        private void fileSize_TextChanged(object sender, EventArgs e)
        {
            //fileSize.Text = fileSize.Text.Replace('.', ',');
            //fileSize.Select(fileSize.Text.Length, 0);
        }
        private void txtStopValue_TextChanged(object sender, EventArgs e)
        {
            //txtStopValue.Text = txtStopValue.Text.Replace('.', ',');
            //txtStopValue.Select(txtStopValue.Text.Length, 0);
        }

    }
    internal class RMCollection<item> : CollectionBase
    {
        internal int Add(item value)
        {
            return List.Add(value);
        }
        internal item this[int index]
        {
            get
            {
                return (item)List[index];
            }
            set
            {
                List[index] = value;
            }
        }
    }
    internal class ProcessMemoryReaderApi
    {
        [DllImport("kernel32.dll")]
        internal static extern int CloseHandle(IntPtr hObject);
        [DllImport("kernel32.dll")]
        internal static extern IntPtr OpenProcess(uint dwDesiredAccess, int bInheritHandle, uint dwProcessId);
        [DllImport("kernel32.dll")]
        internal static extern int ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [In, Out] byte[] buffer, uint size, out IntPtr lpNumberOfBytesRead);

        internal const uint PROCESS_VM_READ = 0x10;
    }
    internal class ProcessMemoryReader
    {
        internal static void saveArrayToFile(byte[] arr, string filename)
        {
            FileStream stream1 = File.OpenWrite(filename);
            stream1.Write(arr, 0, arr.Length);
            stream1.Close();
        }
        internal ProcessMemoryReader()
        {
            m_hProcess = IntPtr.Zero;
        }
        internal void CloseHandle()
        {
            if (ProcessMemoryReaderApi.CloseHandle(m_hProcess) == 0)
            {
                throw new Exception("CloseHandle failed");
            }
        }
        internal void OpenProcess()
        {
            m_hProcess = ProcessMemoryReaderApi.OpenProcess(0x10, 1, (uint)m_ReadProcess.Id);
        }
        internal byte[] ReadProcessMemory(IntPtr MemoryAddress, uint bytesToRead, out int bytesReaded)
        {
            IntPtr ptr1;
            byte[] buffer1 = new byte[bytesToRead];
            ProcessMemoryReaderApi.ReadProcessMemory(m_hProcess, MemoryAddress, buffer1, bytesToRead, out ptr1);
            bytesReaded = ptr1.ToInt32();
            return buffer1;
        }
        internal Process ReadProcess
        {
            get
            {
                return m_ReadProcess;
            }
            set
            {
                m_ReadProcess = value;
            }
        }
        private IntPtr m_hProcess;
        private Process m_ReadProcess;
    }
    internal class Peer
    {
        internal IPAddress IpAddress;
        internal string Peer_ID;
        internal ushort Port;
        internal Peer(byte[] ip, short port)
        {
            Peer_ID = "";
            IpAddress = new IPAddress(ip);
            Port = (ushort)IPAddress.NetworkToHostOrder(port);
            Peer_ID = "";
        }
        internal Peer(string ip, string port, string peer_id)
        {
            Peer_ID = "";
            try
            {
                IpAddress = IPAddress.Parse(ip);
                Port = (ushort)IPAddress.NetworkToHostOrder(short.Parse(port));
                Peer_ID = peer_id;
            }
            catch { }
        }
        public override string ToString()
        {
            if (Peer_ID.Length > 0)
            {
                return (IpAddress + ":" + Port + "(PeerID=" + Peer_ID + ")");
            }
            return (IpAddress + ":" + Port);
        }
    }
    internal class PeerList : List<Peer>
    {
        internal PeerList()
        {
            maxPeersToShow = 5;
        }
        public override string ToString()
        {
            string text1;
            text1 = "(" + Count + ") ";
            foreach (Peer peer1 in this)
            {
                if (peerCounter < maxPeersToShow)
                {
                    text1 = text1 + peer1 + ";";
                }
                peerCounter++;
            }
            return text1;
        }
        internal int maxPeersToShow;
        internal int peerCounter;
    }
}
