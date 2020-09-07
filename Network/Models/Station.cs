using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Web;

namespace Network.Models
{
    public class Station
    {
        [NonSerialized]
        public static int CounterID = 0;

        [NonSerialized]
        public static List<Station> AllStations = new List<Station>();

        [NonSerialized]
        public Channel<Packet> ReceivedPackets = new Channel<Packet>();

        [NonSerialized]
        public List<Message> AchievedMessages = new List<Message>();

        public string AchievedMessagesIDs => String.Join(",", AchievedMessages.Select(message => message.ID).ToArray());

        [NonSerialized]
        public Dictionary<Message,List<Packet>> ProcessingMessages = new Dictionary<Message, List<Packet>>();

        public static bool UseCache = false;

        public int Region = 0;

        public int X { get; set; }

        public int Y { get; set; }

        private string _Name = string.Empty;
        public string Name
        {
            get
            {
                if (!string.IsNullOrEmpty(_Name))
                    return _Name;

                return $"Region: {Region}, Station: {ID}";
            }
            set
            {
                _Name = value;
            }
        }

        public List<Node> Nodes = new List<Node>();
        
        public int ID = 0;

        public Station()
        {
            CounterID++;
            ID = CounterID;
            Worker = new Thread(new ThreadStart(ProcessQueue));
            Worker.Start();
        }

        [NonSerialized]
        public Thread Worker;
        public bool IsReady = true;
        public static bool ProcessingEnabled = true;
        
        public void ProcessVirtualChannel(Packet packet)
        {
            if (!ProcessingMessages.ContainsKey(packet.Message))
                ProcessingMessages.Add(packet.Message, new List<Packet>());

            var messagePackets = ProcessingMessages[packet.Message];
            messagePackets.Add(packet);

            if (messagePackets.Count == packet.Message.PacketsCount)
            {
                //убираем из обработки это сообщение
                ProcessingMessages.Remove(packet.Message);

                //packet.Message.PassedStations.Add(this);

                #region createFailedMessageRequest
                var corruptedPacket = messagePackets.FirstOrDefault(p => p.Size != p.ExpectedSize);
                if (corruptedPacket != null)
                {
                    //создаем новое сообщение, говорим что произошла ошибка ( возможно стоит идти строго по обратному маршруту )
                    messagePackets.Remove(corruptedPacket);
                    ProcessingMessages.Add(packet.Message, messagePackets);
                    new Message(packet.Message, MessageType.DeliveryFailure, this, Message.GetNodeByRoute(this, new Route() { Stations = packet.Message.PassedStations }).LinkedStation, corruptedPacket);

                    //packet.Message.Route.Stations.AddRange(packet.Message.PassedStations);
                }
                #endregion
                #region EndpointAchieved
                else if (packet.Message.Destination == this)
                {
                    AchievedMessages.Add(packet.Message);
                    packet.Message.PassedStations.Add(this);
                    switch (packet.Message.Type)
                    {
                        case MessageType.DeliveryFailure:
                            packet.Message.CompleteDate = DateTime.Now;
                            packet.Message.MessageLog.Add($"Resend packet request delivered: {packet.Message.ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                            packet.Message.LinkedMessage.MessageLog.Add($"Resend request for packet : Size - {packet.Message.LinkedPacket.Size}, Expected size - {packet.Message.LinkedPacket.ExpectedSize} in message: {packet.Message.ID} achieved ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");

                            var channel = Message.GetNodeByRoute(this, new Route() { Stations = packet.Message.PassedStations });

                            corruptedPacket = packet.Message.LinkedPacket;

                            var newPacket = new Packet()
                            {
                                ExpectedSize = corruptedPacket.ExpectedSize,
                                Type = PacketType.Data,
                                Size = Message.Generator.NextDouble() > channel.ErrorProbability ? corruptedPacket.ExpectedSize : Message.Generator.Next(corruptedPacket.ExpectedSize),
                                Message = corruptedPacket.Message,
                                PassedStations = new List<Station>() { this }
                            };
                            
                            packet.Message.LinkedMessage.MessageLog.Add($"Enqueue packet to {channel.LinkedStationID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                            channel.Packets.Enqueue(newPacket);
                            break;
                        case MessageType.RouteRequest:
                            packet.Message.LinkedMessage.Route = new Route()
                            {
                                Stations = packet.Message.PassedStations.Distinct().ToList()
                            };

                            packet.Message.CompleteDate = DateTime.Now;
                            packet.Message.MessageLog.Add($"Route successfully requested : {packet.Message.ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                            packet.Message.LinkedMessage.MessageLog.Add($"Route requested completed: {packet.Message.ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");

                            new Message(packet.Message.LinkedMessage, MessageType.RouteResponse, packet.Message.Destination, packet.Message.Source );
                            break;
                        case MessageType.RouteResponse:
                            packet.Message.LinkedMessage.ReverseRoute = new Route()
                            {
                                Stations = packet.Message.PassedStations.Distinct().ToList()
                            };

                            packet.Message.CompleteDate = DateTime.Now;
                            packet.Message.MessageLog.Add($"Route successfully responsed : {packet.Message.ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                            packet.Message.LinkedMessage.MessageLog.Add($"Route response achieved: {packet.Message.ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");

                            packet.Message.LinkedMessage.CreatePackets(packet.Message.LinkedMessage.GetNodeByRoute(packet.Message.LinkedMessage.Source));
                            break;
                        case MessageType.Data:
                            //packet.Message.CompleteDate = DateTime.Now;
                            packet.Message.CompleteDate = DateTime.Now;
                            packet.Message.MessageLog.Add($"Message successfully delivered : {packet.Message.ID}. ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                            //packet.Message.LinkedMessage.MessageLog.Add($"Route response achieved: {packet.Message.ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                            new Message(packet.Message, MessageType.Success, this, packet.Message.Source);
                            break;
                        case MessageType.Success:
                            packet.Message.MessageLog.Add($"Success response delivered for message : {packet.Message.LinkedMessage.ID}. ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                            packet.Message.LinkedMessage.MessageLog.Add($"Success response achieved : {packet.Message.ID}. ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                            packet.Message.CompleteDate = DateTime.Now;
                            new Message(packet.Message.LinkedMessage, MessageType.ChannelDispose, this, packet.Message.Source);
                            break;
                        case MessageType.ChannelDispose:
                            packet.Message.LinkedMessage.MessageLog.Add($"Channel successfully disposed by message {packet.Message.ID}. ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                            packet.Message.MessageLog.Add($"Channel dispose completed for message : {packet.Message.LinkedMessage.ID} . ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                            packet.Message.CompleteDate = DateTime.Now;
                            break;

                    }
                }
                #endregion
                #region TransitMessage
                else
                {
                    packet.Message.MessageLog.Add($"Message transit station achieved : {ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");

                    packet.Message.RecalcRoute(this, messagePackets);
                }
            }
        }
        public void ProcessDatagram(Packet packet)
        {
            #region createFailedMessageRequest
            var corruptedPacket = packet.Size != packet.ExpectedSize ? packet : null;
            if (corruptedPacket != null)
            {
                //создаем новое сообщение, говорим что произошла ошибка ( возможно стоит идти строго по обратному маршруту )
                new Message(packet.Message, MessageType.DeliveryFailure, this, Message.GetNodeByRoute(this, new Route() { Stations = packet.PassedStations }).LinkedStation, corruptedPacket);
                
            }
            #endregion
            #region EndpointAchieved
            else if (packet.Message.Destination == this)
            {
                if (!ProcessingMessages.ContainsKey(packet.Message))
                    ProcessingMessages.Add(packet.Message, new List<Packet>());

                var messagePackets = ProcessingMessages[packet.Message];
                messagePackets.Add(packet);

                if (messagePackets.Count == packet.Message.PacketsCount)
                {
                    //убираем из обработки это сообщение
                    ProcessingMessages.Remove(packet.Message);
                    AchievedMessages.Add(packet.Message);
                    messagePackets.ForEach(p =>
                    {
                        p.PassedStations.Add(this);
                    });

                    switch (packet.Message.Type)
                    {
                        case MessageType.DeliveryFailure:
                            packet.Message.CompleteDate = DateTime.Now;
                            packet.Message.MessageLog.Add($"Resend packet request delivered: {packet.Message.ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                            packet.Message.LinkedMessage.MessageLog.Add($"Resend request for packet : Size - {packet.Message.LinkedPacket.Size}, Expected size - {packet.Message.LinkedPacket.ExpectedSize} in message: {packet.Message.ID} achieved ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");

                            var channel = Message.GetNodeByRoute(this, new Route() { Stations = packet.PassedStations });

                            corruptedPacket = packet.Message.LinkedPacket;

                            var newPacket = new Packet()
                            {
                                ExpectedSize = corruptedPacket.ExpectedSize,
                                Type = PacketType.Data,
                                Size = Message.Generator.NextDouble() > channel.ErrorProbability ? corruptedPacket.ExpectedSize : Message.Generator.Next(corruptedPacket.ExpectedSize),
                                Message = corruptedPacket.Message,
                                PassedStations = new List<Station>() { this }
                            };

                            packet.Message.LinkedMessage.MessageLog.Add($"Enqueue packet to {channel.LinkedStationID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                            channel.Packets.Enqueue(newPacket);
                            break;
                        case MessageType.Data:
                            //packet.Message.CompleteDate = DateTime.Now;
                            packet.Message.CompleteDate = DateTime.Now;
                            packet.Message.MessageLog.Add($"Message successfully delivered : {packet.Message.ID}. ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                            //packet.Message.LinkedMessage.MessageLog.Add($"Route response achieved: {packet.Message.ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                            new Message(packet.Message, MessageType.Success, this, packet.Message.Source);
                            break;
                        case MessageType.Success:
                            packet.Message.MessageLog.Add($"Success response delivered for message : {packet.Message.LinkedMessage.ID}. ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                            packet.Message.LinkedMessage.MessageLog.Add($"Success response achieved : {packet.Message.ID}. ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                            packet.Message.CompleteDate = DateTime.Now;
                            break;
                    }
                }
            }
            #endregion
            #region TransitMessage
            else
            {
                packet.Message.MessageLog.Add($"Message transit station achieved : {ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");

                packet.Message.RecalcRoute(this, new List<Packet>() { packet });
            }
            #endregion
        }
        public void ProcessQueue()
        {
            while (ProcessingEnabled)
            {
                if (ReceivedPackets.Count != 0 && IsReady)
                {
                    IsReady = false;
                    var packet = ReceivedPackets.Dequeue();

                    switch (Message.Mode)
                    {
                        case "Datagram":
                            ProcessDatagram(packet);
                            break;
                        case "VirtualChannel":
                            ProcessVirtualChannel(packet);
                            break;
                    }

                    IsReady = true;
                }
                else
                {
                    Thread.Sleep(100);
                }
            }
        }
        //available nodes for each destination
        [NonSerialized]
        public Dictionary<Station, List<Route>> AvailableRoutes = new Dictionary<Station, List<Route>>();
        
        public string GetImage()
        {
            if (Nodes.Any(node => node.LinkedStation.Region != Region))
                return "/images/System-Firewall-2-icon.png";
            else
            {
                int type = (Region * ID % 5) + 1;
                switch (type)
                {
                    case 1:
                        return "/images/Hardware-Laptop-1-icon.png";
                    case 2:
                        return "/images/Hardware-My-Computer-3-icon.png";
                    case 3:
                        return "/images/Hardware-My-PDA-02-icon.png";
                    case 4:
                        return "/images/Network-Pipe-icon.png";
                    default:
                        return "/images/Hardware-Printer-Blue-icon.png";
                }
            }
        }

        public List<Route> GetRoutes(Station Destination, List<Station> usedStations)
        {
            usedStations.Add(this);

            List<Route> routes = new List<Route>();

            var foundNode = Nodes.FirstOrDefault(node => node.LinkedStation == Destination);

            if (foundNode == null)
                //игнорируем станции, которые уже участвовали в маршруте
                Nodes.FindAll(node => !usedStations.Any(station => station == node.LinkedStation)).ForEach(node =>
                {
                    List<Station> stations = new List<Station>();
                    stations.AddRange(usedStations);
                    List<Route> availableRoutes = null;
                    if (UseCache && node.LinkedStation.AvailableRoutes.ContainsKey(Destination))
                    {//if exists in station cache
                        availableRoutes = node.LinkedStation.AvailableRoutes[Destination];
                        availableRoutes.ForEach(route =>
                        {
                            List<Station> bufStations = new List<Station>();
                            bufStations.AddRange(route.Stations);
                            bufStations.AddRange(stations);
                            routes.Add(new Route() { Stations = bufStations });
                        });
                    }
                    else
                    {
                        availableRoutes = node.LinkedStation.GetRoutes(Destination, stations);
                    }

                    if (availableRoutes.Count > 0)
                    {
                        routes.AddRange(availableRoutes);
                        if (UseCache && !(node.LinkedStation.AvailableRoutes.ContainsKey(Destination)))
                        {
                            node.LinkedStation.AvailableRoutes.Add(Destination, new List<Route>());
                            node.LinkedStation.AvailableRoutes[Destination].AddRange(availableRoutes);
                        }
                    }
                });
            else
            {
                usedStations.Add(foundNode.LinkedStation);
                routes.Add(new Route() { Stations = usedStations.Distinct().ToList() });
            }

            return routes.ToList();
        }
    }
    public class Route
    {
        //public Node Node { get; set; }
        [NonSerialized]
        public List<Station> Stations = new List<Station>();
        public string RouteSheet => String.Join(",", Stations.Select(station => station.ID).ToArray());
        public int GetExpectedDelay(Node channel)
        {
            return Stations.Count * channel.ExpectedDelay;
        }
        public int StationsCount//add method with param currentStation/Node
        {
            get
            {
                return Stations.Count;// * Node.ExpectedDelay;
            }
        }

    }

    public class Node
    {
        [NonSerialized]
        public Station LinkedStation = null;
        public int LinkedStationID
        {
            get
            {
                return LinkedStation != null ? LinkedStation.ID : 0;
            }
            set
            {
                LinkedStation = Station.AllStations.FirstOrDefault(station => station.ID == value);
            }
        }
        
        public Node GetReverseNode()
        {
            return LinkedStation.Nodes.FirstOrDefault(node => node.LinkedStationID == StationID);
        }
        
        [NonSerialized]
        public Station Station = null;
        public int StationID
        {
            get
            {
                return Station != null ? Station.ID : 0;
            }
            set
            {
                Station = Station.AllStations.FirstOrDefault(station => station.ID == value);
            }
        }
        public Channel<Packet> Packets = new Channel<Packet>();
        //distance
        public int Weight { get; set; }
        public int ExpectedDelay
        {
            get
            {
                return (Weight * Packets.Count * Packet.MaxSize);
            }
        }
        [NonSerialized]
        public Thread Worker;
        //public bool IsReady = true;

        public static Dictionary<Node,Semaphore> Semaphores = new Dictionary<Node,Semaphore>();

        public bool Duplex = true;
        //public Semaphore Semaphore = new Semaphore(1, 1);
        public double ErrorProbability = 0.2;
        public static bool ProcessingEnabled = true;
        public void ProcessQueue()
        {
            while (ProcessingEnabled)
            {
                if (Packets.Count != 0)
                {
                    var semaphore = Semaphores[this];
                    semaphore.WaitOne();

                    var packet = Packets.Dequeue();
                    Thread.Sleep(Weight * packet.Size);
                    packet.Send(LinkedStation);

                    semaphore.Release();
                }
                else
                {
                    Thread.Sleep(100);
                }
            }
        }
        public Node()
        {
            Worker = new Thread(new ThreadStart(ProcessQueue));
            Worker.Start();
        }
    }

    public class Message
    {
        [NonSerialized]
        public static int CounterID = 0;

        [NonSerialized]
        public static List<Message> AllMessages = new List<Message>();

        [NonSerialized]
        public static Random Generator = new Random();

        [NonSerialized]
        public static string Mode = "Datagram";

        public int ID = 0;
        public Route Route { get; set; }
        public Route ReverseRoute { get; set; }
        public int PacketsCount = 0;
        public int Size = 0;
        private Packet _LinkedPacket = null;
        public Packet LinkedPacket
        {
            get
            {
                return _LinkedPacket;
                //return AllMessages.FirstOrDefault(message => message.ID == LinkedMessageID);
            }
            set
            {
                _LinkedPacket = value;
            }
        }
        private Message _LinkedMessage = null;
        public Message LinkedMessage
        {
            get
            {
                return _LinkedMessage;
                //return AllMessages.FirstOrDefault(message => message.ID == LinkedMessageID);
            }
            set
            {
                _LinkedMessage = value;
            }
        }

        [NonSerialized]
        public DateTime CreateDate = DateTime.Now;
        [NonSerialized]
        public DateTime CompleteDate = DateTime.MinValue;

        public string Status
        {
            get
            {
                if (CompleteDate != DateTime.MinValue)
                    return "Completed";

                return "In Processing";
            }
        }

        public double TimeSpent
        {
            get
            {
                if (CompleteDate != DateTime.MinValue)
                    return CompleteDate.Subtract(CreateDate).TotalMilliseconds;
                else
                    return DateTime.Now.Subtract(CreateDate).TotalMilliseconds;
            }
        }


        public List<string> MessageLog { get; set; }

        [NonSerialized]
        public List<Station> PassedStations = new List<Station>();

        public Station Destination = null;
        public Station Source = null;

        public MessageType Type = MessageType.Data;

        public void InitMessage(Station source, Station destination)
        {
            MessageLog = new List<string>();
            CounterID++;
            ID = CounterID;
            Destination = destination;
            Source = source;
            AllMessages.Add(this);
        }
        public Node SetRoute(Station from, Packet packet = null)
        {
            #region findRoute
            List<Route> availableRoutes = from.GetRoutes(Destination, new List<Station>());
            List<Route> acceptableRoutes = new List<Route>();
            availableRoutes.ForEach(availableRoute =>
            {
                var hasPassedStation = false;
                availableRoute.Stations.ForEach(routeStation =>
                {
                    if ((packet != null && packet.PassedStations.Any(passedStation => passedStation.ID == routeStation.ID))
                        || (packet == null && PassedStations.Any(passedStation => passedStation.ID == routeStation.ID)))
                    {
                        hasPassedStation = true;
                        return;
                    }
                });
                if (!hasPassedStation)
                {
                    acceptableRoutes.Add(availableRoute);
                }
            });

            if (packet != null)
                packet.PassedStations.Add(from);
            else
                PassedStations.Add(from);

            if (acceptableRoutes.Count == 0)
            {
                CompleteDate = DateTime.Now;
                MessageLog.Add($"Delivery failed. Can't find route ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                return null;
            }

            var route = acceptableRoutes.OrderBy(r =>
                r.GetExpectedDelay(from.Nodes.FirstOrDefault(node =>
                    r.Stations.FirstOrDefault(s =>
                        s.ID == node.LinkedStationID
                    ) != null)
                )
            ).FirstOrDefault();

            if (packet != null)
            {
                packet.Route = route;
                MessageLog.Add($"Route for packet : {route.RouteSheet}. ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");

                return GetNodeByRoute(from, packet.Route);
            }
            else
            {
                Route = route;
                MessageLog.Add($"Route : {route.RouteSheet}. ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");

                return GetNodeByRoute(from, Route);
            }

            #endregion
        }
        public static Node GetNodeByRoute(Station from, Route route)
        {
            return from.Nodes.FirstOrDefault(node =>
                route.Stations.FirstOrDefault(station =>
                    station.ID == node.LinkedStationID
                ) != null
            );
        }

        public Node GetNodeByRoute(Station from)
        {
            if (!PassedStations.Any(station => station.ID == from.ID))
                PassedStations.Add(from);

            Route.Stations.RemoveAll(s => s.ID == from.ID);
            var channel = Message.GetNodeByRoute(from, this.Route);

            return channel;
        }

        public void RecalcRoute(Station currentStation, List<Packet> packets)
        {
            Node channel = null;
            if (Mode == "VirtualChannel")
            {
                if (Type != MessageType.RouteRequest && Type != MessageType.RouteResponse)
                    channel = GetNodeByRoute(currentStation);
                else
                    channel = SetRoute(currentStation);
            }

            packets.ForEach(packet =>
            {

                if (Mode == "Datagram")
                    channel = SetRoute(currentStation, packet);
                
                if (packet.Type != PacketType.Service)
                {
                    packet.ExpectedSize = packet.Size;
                    packet.Size = Generator.NextDouble() > channel.ErrorProbability ? packet.Size : Generator.Next(packet.Size);
                }

                if (channel == null)
                {
                    CompleteDate = DateTime.Now;
                    MessageLog.Add($"Failed to enqueue packet. Try one more time. ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                }

                MessageLog.Add($"Enqueue packet to {channel.LinkedStationID}. Size: {packet.Size} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");

                channel.Packets.Enqueue(packet);
            });
            #endregion
        }
        public void CreatePackets(Node channel = null)
        {
            PacketsCount = 0;
            #region createPackets
            var remainingSize = Size;
            while (remainingSize > 0)
            {
                var packetSize = remainingSize > Packet.MaxSize ? Packet.MaxSize : remainingSize;


                Packet packet = new Packet()
                {
                    ExpectedSize = packetSize,
                    Type = PacketType.Data,
                    Message = this
                };

                if (Mode == "Datagram")
                {
                    channel = SetRoute(Source, packet);
                }

                packet.Size = Generator.NextDouble() > channel.ErrorProbability ? packetSize : Generator.Next(packetSize);


                remainingSize -= packetSize;
                MessageLog.Add($"Enqueue packet to {channel.LinkedStationID}. Size: {packet.Size} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");

                PacketsCount++;
                channel.Packets.Enqueue(packet);
            }
            #endregion
        }
        public Message(Message linkedMessage, MessageType type, Station source, Station destination, Packet linkedPacket = null)
        {
            Type = type;
            LinkedMessage = linkedMessage;
            LinkedPacket = linkedPacket;

            InitMessage(source, destination);
            Node channel = null;

            var servicePacket = new Packet()
            {
                ExpectedSize = Packet.ServicePacketSize,
                Size = Packet.ServicePacketSize,
                Type = PacketType.Service,
                Message = this
            };
            
            switch (Type)
            {
                case MessageType.RouteRequest:
                    channel = SetRoute(Source, Mode == "Datagram" ? servicePacket : null);
                    MessageLog.Add($"Create route request for message: {LinkedMessage.ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                    LinkedMessage.MessageLog.Add($"Route request created: {ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                    break;
                case MessageType.RouteResponse:
                    channel = SetRoute(Source, Mode == "Datagram" ? servicePacket : null);
                    MessageLog.Add($"Create route response for message: {LinkedMessage.ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                    LinkedMessage.MessageLog.Add($"Route response created: {ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                    break;
                case MessageType.Success:
                    if (Mode == "VirtualChannel")
                    {
                        Route = new Route();
                        LinkedMessage.ReverseRoute.Stations.ForEach(s =>
                        {
                            Route.Stations.Add(s);
                        });
                    }
                    channel = Mode == "VirtualChannel" ? GetNodeByRoute(Source) : SetRoute(Source, servicePacket);
                    MessageLog.Add($"Create success response for {(LinkedPacket != null ? "packet in message" : "message")} : {LinkedMessage.ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                    LinkedMessage.MessageLog.Add($"Success response created: {ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                    break;
                case MessageType.ChannelDispose:
                    if (Mode == "VirtualChannel")
                    {
                        Route = new Route();
                        LinkedMessage.PassedStations.Distinct().ToList().ForEach(s =>
                        {
                            Route.Stations.Add(s);
                        });
                    }
                    channel = Mode == "VirtualChannel" ? GetNodeByRoute(Source) : SetRoute(Source, servicePacket);
                    MessageLog.Add($"Create channel dispose request for message: {LinkedMessage.ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                    LinkedMessage.MessageLog.Add($"Channel dispose request created: {ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                    break;
                case MessageType.DeliveryFailure:
                    if (Mode == "VirtualChannel")
                    {
                        Route = new Route() { Stations = new List<Station>() { Source, Destination } };
                    }
                    //channel = GetNodeByRoute(this, new Route() { Stations = packet.Message.PassedStations })
                    channel = Mode == "VirtualChannel" ? GetNodeByRoute(Source) : SetRoute(Source, servicePacket);
                    MessageLog.Add($"Request resend for {(LinkedPacket != null ? "packet in message" : "message")}: {LinkedMessage.ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                    LinkedMessage.MessageLog.Add($"Resend request created: {ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
                    break;
            }

            
            
            PacketsCount++;
            channel.Packets.Enqueue(servicePacket);
            MessageLog.Add($"Enqueue packet to {channel.LinkedStationID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");

            Size = Packet.ServicePacketSize;
        }
        public Message(int size, Station source, Station destination)
        {
            Size = size;

            if (Mode == "VirtualChannel")
            {
                InitMessage(source, destination);
                MessageLog.Add($"Create message: {ID}. Size: {Size}. Source: {Source.ID}. Destination: {Destination.ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");

                //Create route request
                new Message(this, MessageType.RouteRequest, Source, Destination);

            }
            else
            {
                InitMessage(source, destination);
                //var channel = SetRoute(source);

                MessageLog.Add($"Create message: {ID}. Size: {Size}. Source: {Source.ID}. Destination: {Destination.ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");

                CreatePackets();
            }

        }
    }
    public class Packet
    {
        public static int MaxSize = 64;
        public static int ServicePacketSize = 8;
        [NonSerialized]
        public static ConcurrentBag<Packet> AllPackets = new ConcurrentBag<Packet>();
        [NonSerialized]
        public List<Station> PassedStations = new List<Station>();
        
        public Route Route { get; set; }

        public PacketType Type { get; set; }
        public Message Message { get; set; }
        public int Size { get; set; }
        public int ExpectedSize { get; set; }
        public Packet()
        {
            AllPackets.Add(this);
        }
        public void Send(Station nextStation)
        {
            Message.MessageLog.Add($"Send packet to {nextStation.ID} ( {DateTime.Now.ToString("dd:MM:yyyy HH:mm:ss.ffff")} )");
            nextStation.ReceivedPackets.Enqueue(this);
        }
    }
    public enum PacketType
    {
        Service = 0,
        Data = 1,
    }
    public enum MessageType
    {
        RouteRequest = 0,
        RouteResponse = 1,
        DeliveryFailure = 2,
        Data = 3,
        Success = 4,
        ChannelDispose = 5
    }
    public class Channel<T>
    {
        private readonly Queue<T> _queue = new Queue<T>();

        public void Enqueue(T item)
        {
            lock (_queue)
            {
                _queue.Enqueue(item);
                if (_queue.Count == 1)
                    Monitor.PulseAll(_queue);
            }
        }

        public T Dequeue()
        {
            lock (_queue)
            {
                while (_queue.Count == 0)
                    Monitor.Wait(_queue);

                return _queue.Dequeue();
            }
        }

        public T Peek()
        {
            lock (_queue)
            {
                while (_queue.Count == 0)
                    Monitor.Wait(_queue);

                return _queue.Peek();
            }
        }

        public int Count
        {
            get
            {
                lock (_queue)
                {
                    return _queue.Count;
                }
            }
        }
    }
}