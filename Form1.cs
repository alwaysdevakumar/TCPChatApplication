using ChatCommonn;
using SimpleTCP;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TCPChatApplication
{
    public partial class Form1 : Form
    {
        SimpleTcpServer server;
        Dictionary<string, UserSession> activeUsers = new Dictionary<string, UserSession>();

        private TextBox txtLogs;
        private Button btnStart;
        private Button btnStop;
        private Label lblStatus;
        private ListBox lstUsers;

        public Form1()
        {
            InitializeComponent();
            Form1_Load(null, null);
            this.Text = "TCP Chat Server";
            this.ClientSize = new Size(650, 400);

            Label lbl = new Label()
            {
                Text = "Server Logs",
                Location = new Point(10, 10),
                AutoSize = true
            };
            this.Controls.Add(lbl);

            txtLogs = new TextBox()
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(10, 30),
                Size = new Size(450, 300)
            };
            this.Controls.Add(txtLogs);

            lstUsers = new ListBox()
            {
                Location = new Point(480, 30),
                Size = new Size(150, 300)
            };
            this.Controls.Add(lstUsers);

            btnStart = new Button()
            {
                Text = "Start Server",
                Location = new Point(10, 340),
                Size = new Size(120, 30)
            };
            btnStart.Click += btnStart_Click;
            this.Controls.Add(btnStart);

            btnStop = new Button()
            {
                Text = "Stop Server",
                Location = new Point(140, 340),
                Size = new Size(120, 30)
            };
            btnStop.Click += btnStop_Click;
            this.Controls.Add(btnStop);

            lblStatus = new Label()
            {
                Text = "Server not started",
                Location = new Point(280, 345),
                AutoSize = true
            };
            this.Controls.Add(lblStatus);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            server = new SimpleTcpServer();
            server.Delimiter = 0x13;
            server.StringEncoder = System.Text.Encoding.UTF8;
            server.DataReceived += Server_DataReceived;
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            try
            {
                var ip = IPAddress.Parse("127.0.0.1");
                server.Start(ip, 9000);
                lblStatus.Text = "✅ Server started on 127.0.0.1:9000";
                Log("Server started...");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error starting server: " + ex.Message);
            }
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            if (server.IsStarted)
                server.Stop();
        }

        private void Server_DataReceived(object sender, SimpleTCP.Message e)
        {
            try
            {
                var packet = JsonSerializer.Deserialize<Packet>(e.MessageString);
                if (packet == null) return;

                switch (packet.Type)
                {
                    case "LOGIN_REQ":
                        HandleLogin(e, packet);
                        break;
                    case "DM":
                        HandleDirectMessage(packet);
                        break;
                    case "MULTI":
                        HandleMultiMessage(packet);
                        break;
                    case "BROADCAST":
                        HandleBroadcast(packet);
                        break;
                }
            }
            catch
            {
                // Invalid JSON - close session
                e.ReplyLine("Malformed JSON, session closed");
            }
        }

        private void HandleLogin(SimpleTCP.Message e, Packet packet)
        {
            var validUsers = new Dictionary<string, string>
            {
                { "user1", "pass1" },
                { "user2", "pass2" },
                { "user3", "pass3" }
            };

            Packet response = new Packet() { Type = "LOGIN_RESP" };

            if (validUsers.TryGetValue(packet.Username, out string pass) && pass == packet.Password)
            {
                response.Ok = true;
                lock (activeUsers)
                {
                    activeUsers[packet.Username] = new UserSession { Username = packet.Username, Client = e.TcpClient };
                }
                Log($"{packet.Username} logged in");
            }
            else
            {
                response.Ok = false;
                response.Reason = "Invalid credentials";
            }

            string json = JsonSerializer.Serialize(response);
            e.ReplyLine(json);
        }
        private void SendMessageToClient(TcpClient client, string message)
        {
            if (client == null || !client.Connected) return;

            try
            {
                NetworkStream stream = client.GetStream();
                byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                stream.Write(data, 0, data.Length);
                stream.Flush();
            }
            catch (Exception ex)
            {
                Log($"Error sending to client: {ex.Message}");
            }
        }


        private void HandleDirectMessage(Packet packet)
        {
            if (activeUsers.TryGetValue(packet.To, out UserSession receiver))
            {
                string json = JsonSerializer.Serialize(packet);
                //receiver.Client.WriteLineAndGetReply(json, TimeSpan.FromSeconds(3));
                SendMessageToClient(receiver.Client, json);

                Log($"{packet.Username} → {packet.To}: {packet.Message}");
            }
        }

        private void HandleMultiMessage(Packet packet)
        {
            foreach (var toUser in packet.ToList)
            {
                if (activeUsers.TryGetValue(toUser, out UserSession receiver))
                {
                    string json = JsonSerializer.Serialize(packet);
                   // receiver.Client.WriteLineAndGetReply(json, TimeSpan.FromSeconds(3));
                }
            }
            Log($"{packet.Username} → MULTI({string.Join(",", packet.ToList)}): {packet.Message}");
        }


        private void HandleBroadcast(Packet packet)
        {
            foreach (var kvp in activeUsers)
            {
                if (kvp.Key == packet.Username) continue; // don't send to sender

                string json = JsonSerializer.Serialize(packet);

                // Send using NetworkStream instead of WriteLineAndGetReply
                SendMessageToClient(kvp.Value.Client, json);
            }

            Log($"{packet.Username} → BROADCAST: {packet.Message}");
        }
        



        private void Log(string text)
        {
            txtLogs.Invoke((MethodInvoker)delegate
            {
                txtLogs.AppendText($"{DateTime.Now:T}: {text}\r\n");
            });
        } 
    }
}
