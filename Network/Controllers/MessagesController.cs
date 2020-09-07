using Network.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace Network.Controllers
{
    public class MessagesController : ApiController
    {
        [Route("api/messages/all")]
        [HttpGet]
        public List<Message> GetMessages()
        {
            return Message.AllMessages;
        }


        [Route("api/messages/result")]
        [HttpGet]
        public string GetResult()
        {
            string result = string.Empty;
            
            result += $"<tr><td>Duplex channels count</td><td>{Station.AllStations.Sum(station => station.Nodes.Count(node => node.Duplex))}</td></tr>";
            result += $"<tr><td>Semi-Duplex channels count</td><td>{Station.AllStations.Sum(station => station.Nodes.Count(node => !node.Duplex))}</td></tr>";
            result += $"<tr><td>Avg. error probability</td><td>{Station.AllStations.Sum(station => station.Nodes.Sum(node => node.ErrorProbability)) / Station.AllStations.Sum(station => station.Nodes.Count)}</td></tr>";
            result += $"<tr><td>Mode</td><td>{Message.Mode}</td></tr>";
            result += $"<tr><td>Message size</td><td>{Message.AllMessages.FindAll(message => message.Type == MessageType.Data).Max(message => message.Size)}</td></tr>";
            result += $"<tr><td>Data packet size</td><td>{Packet.MaxSize}</td></tr>";
            result += $"<tr><td>Service packet size</td><td>{Packet.ServicePacketSize}</td></tr>";
            result += $"<tr><td>Messages</td><td>{Message.AllMessages.FindAll(message => message.Type == MessageType.Data).Count}</td></tr>";
            result += $"<tr><td>Avg. Time for data packets</td><td>{Message.AllMessages.FindAll(message => message.Type == MessageType.Data).Average(message => Convert.ToInt32(message.TimeSpent))}</td></tr>";
            result += $"<tr><td>Avg. Time for service packets</td><td>{Message.AllMessages.FindAll(message => message.Type != MessageType.Data).Average(message => Convert.ToInt32(message.TimeSpent))}</td></tr>";
            result += $"<tr><td>Test first message create time</td><td>{Message.AllMessages.Min(message => message.CreateDate)}</td></tr>";
            result += $"<tr><td>Test last message complete time</td><td>{Message.AllMessages.Max(message => message.CompleteDate)}</td></tr>";
            result += $"<tr><td>Total test time</td><td>{Message.AllMessages.Max(message => message.CompleteDate).Subtract(Message.AllMessages.Min(message => message.CreateDate)).TotalMilliseconds}</td></tr>";
            result += $"<tr><td>Min message delivery time</td><td>{Message.AllMessages.Min(message => Convert.ToInt32(message.TimeSpent))}</td></tr>";
            result += $"<tr><td>Max message delivery time</td><td>{Message.AllMessages.Max(message => Convert.ToInt32(message.TimeSpent))}</td></tr>";
            result += $"<tr><td>Data packets</td><td>{Packet.AllPackets.ToList().FindAll(packet => packet.Message.Type == MessageType.Data).Count}</td></tr>";
            result += $"<tr><td>Total service packets</td><td>{Packet.AllPackets.ToList().FindAll(packet => packet.Message.Type != MessageType.Data).Count}</td></tr>";
            result += $"<tr><td>Channel dispose service packets</td><td>{Packet.AllPackets.ToList().FindAll(packet => packet.Message.Type == MessageType.ChannelDispose).Count}</td></tr>";
            result += $"<tr><td>Delivery failure service packets</td><td>{Packet.AllPackets.ToList().FindAll(packet => packet.Message.Type == MessageType.DeliveryFailure).Count}</td></tr>";
            result += $"<tr><td>Route request service packets</td><td>{Packet.AllPackets.ToList().FindAll(packet => packet.Message.Type == MessageType.RouteRequest).Count}</td></tr>";
            result += $"<tr><td>Route response service packets</td><td>{Packet.AllPackets.ToList().FindAll(packet => packet.Message.Type == MessageType.RouteResponse).Count}</td></tr>";
            result += $"<tr><td>Success service packets</td><td>{Packet.AllPackets.ToList().FindAll(packet => packet.Message.Type == MessageType.Success).Count}</td></tr>";
            result += $"<tr><td>Data bytes</td><td>{Packet.AllPackets.ToList().FindAll(packet => packet.Message.Type == MessageType.Data && packet.Size == packet.ExpectedSize).Sum(packet => packet.Size)}</td></tr>";
            result += $"<tr><td>Corrupted packets size ( expected )</td><td>{Packet.AllPackets.ToList().FindAll(packet => packet.Message.Type == MessageType.Data && packet.Size != packet.ExpectedSize).Sum(packet => packet.Size)} ( {Packet.AllPackets.ToList().FindAll(packet => packet.Message.Type == MessageType.Data && packet.Size != packet.ExpectedSize).Sum(packet => packet.ExpectedSize)} )</td></tr>";
            result += $"<tr><td>Service bytes</td><td>{Packet.AllPackets.ToList().FindAll(packet => packet.Message.Type != MessageType.Data).Sum(packet => packet.Size)}</td></tr>";
            return result;
        }

        // GET api/<controller>/5
        public Message Get(int id)
        {
            return Message.AllMessages.FirstOrDefault(message => message.ID == id);
        }

        // POST api/<controller>
        public void Post([FromBody]MessageRequest request )
        {
            if (request.Size <= 0 || request.Count <= 0)
                return;

            var stationsCount = Station.AllStations.Count;
            for (int i = 0; i < request.Count; i++)
            {
                if (request.Source != null && request.Destination != null)
                    new Message(request.Size, request.Source, request.Destination);
                else
                {
                    Station source = Station.AllStations[Message.Generator.Next(0, stationsCount)];
                    Station destination = Station.AllStations[Message.Generator.Next(0, stationsCount)];
                    
                    while (destination == source)
                        destination = Station.AllStations[Message.Generator.Next(0, stationsCount)];

                    new Message(request.MaxSize != default(int) ? Message.Generator.Next(1, request.MaxSize) : request.Size, source, destination);
                }
            }

        }

        //SET SYSTEM SETTINGS
        public void Put(string mode, int maxPacketSize, int servicePacketSize, double errorProbability, int duplex)
        {
            if (Message.AllMessages.Any(message => message.Status != "Completed"))
                return;

            if(errorProbability != -1 || duplex != -1)
            {
                Station.AllStations.ForEach(station =>
                {
                    station.Nodes.ForEach(node =>
                    {
                        if (duplex != -1)
                            node.Duplex = duplex == 1 ? true : false;

                        if(errorProbability != -1)
                            node.ErrorProbability = errorProbability < 1 && errorProbability >= 0 ? errorProbability : 0;
                    });
                });
            }

            Packet.MaxSize = maxPacketSize;
            Packet.ServicePacketSize = servicePacketSize;
            Message.Mode = mode;
        }

        // DELETE ALL MESSAGES
        public void Delete()
        {
            if (Message.AllMessages.Any(message => message.Status != "Completed"))
                return;

            Station.AllStations.ForEach(station =>
            {
                station.AchievedMessages.Clear();
            });

            Packet.AllPackets = new ConcurrentBag<Packet>();
            Message.AllMessages.Clear();
        }
    }
    public class MessageRequest {
        public int Size;
        public int MaxSize;
        public Station Source
        {
            get
            {
                return Station.AllStations.FirstOrDefault(station => station.ID == SourceID);
            }
        }
        public Station Destination
        {
            get
            {
                return Station.AllStations.FirstOrDefault(station => station.ID == DestinationID);
            }
        }
        public int Count;
        public int SourceID;
        public int DestinationID;
    }
}