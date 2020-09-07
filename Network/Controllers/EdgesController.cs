using Network.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Web.Http;

namespace Network.Controllers
{
    public class EdgesController : ApiController
    {
        // GET ALL EDGES
        public List<VisEdge> Get()
        {
            List<VisEdge> edges = new List<VisEdge>();
            Station.AllStations.ForEach(station =>
            {
                station.Nodes.ForEach(node =>
                {
                    var edge = new VisEdge() {
                        from = station.ID,
                        to = node.LinkedStation.ID,
                        label = node.Weight.ToString(),
                        length = node.Weight * 5,
                        width = 1 + (node.ExpectedDelay / 1000),
                        title = $"Packets count: {node.Packets.Count}<br>ExpectedDelay for new packets: {node.ExpectedDelay}<br>Mode: {(node.Duplex ? "Duplex" : "Semi-Duplex")}<br>Error probability: {node.ErrorProbability}"
                    };

                    if (edge.width > VisEdge.MaxWidth)
                        edge.width = VisEdge.MaxWidth;

                    edges.Add(edge);
                });
            });
            return edges;
        }

        // GET ROUTE
        public List<Route> Get(int sourceID,int destinationID)
        {
            var source = Station.AllStations.FirstOrDefault(station => station.ID == sourceID);
            var destination = Station.AllStations.FirstOrDefault(station => station.ID == destinationID);

            return source.GetRoutes(destination,new List<Station>());
        }
        
        // POST CREATE EDGE
        public void Post([FromBody]List<Link> values)
        {
            values.ForEach(value =>
            {
                if (value?.Source != null && value?.Destination != null && !value.Weight.Equals(default(int)))
                {
                    if (value.Source.Nodes.Any(node => node.Packets.Count > 0 || (node?.GetReverseNode() != null && node.GetReverseNode().Packets.Count > 0)))
                        return;

                    if (value.Destination.Nodes.Any(node => node.Packets.Count > 0 || (node?.GetReverseNode() != null && node.GetReverseNode().Packets.Count > 0) ) )
                        return;

                    Node nodeFrom = value.Source.Nodes.FirstOrDefault(node => node.LinkedStationID == value.DestinationID);
                    if (nodeFrom == null)
                    {
                        nodeFrom = new Node() { LinkedStation = value.Destination, Station = value.Source };
                        value.Source.Nodes.Add(nodeFrom);
                    }
                    nodeFrom.Weight = value.Weight;
                    nodeFrom.ErrorProbability = value.ErrorProbability;
                    nodeFrom.Duplex = value.Duplex;

                    Node nodeTo = value.Destination.Nodes.FirstOrDefault(node => node.LinkedStationID == value.SourceID);
                    if (nodeTo == null)
                    {
                        nodeTo = new Node() { LinkedStation = value.Source, Station = value.Destination };
                        value.Destination.Nodes.Add(nodeTo);
                    }
                    nodeTo.Weight = value.Weight;
                    nodeTo.ErrorProbability = value.ErrorProbability;
                    nodeTo.Duplex = value.Duplex;

                    Node.Semaphores.Remove(nodeFrom);
                    Node.Semaphores.Remove(nodeTo);

                    Node.Semaphores.Add(nodeFrom, new Semaphore(1, 1));
                    Node.Semaphores.Add(nodeTo, value.Duplex ? new Semaphore(1,1) : Node.Semaphores[nodeFrom]);
                }
            });
        }
        /*
        // PUT UPDATE EDGE
        public void Put(int id, [FromBody]string value)
        {

        }
        */

        // DELETE EDGE
        public void Delete([FromBody]List<Link> values)
        {
            //Maybe it's not importnant
            if (Message.AllMessages.Any(message => message.Status != "Completed"))
                return;

            values.ForEach(value =>
            {
                if (value?.Source != null && value?.Destination != null)
                {
                    value.Source.Nodes.RemoveAll(node => node.LinkedStationID == value.DestinationID);
                    value.Destination.Nodes.RemoveAll(node => node.LinkedStationID == value.SourceID);
                }
            });
        }
    }
    public class Link
    {
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
        public int SourceID = 0;
        public int DestinationID = 0;
        public int Weight { get; set; }
        public double ErrorProbability = 0;
        public bool Duplex = true;
    }
    public class VisEdge {
        public int from { get; set; }
        public int to { get; set; }
        public string arrows = "to";
        public string label { get; set; }
        public int length { get; set; }
        public int width { get; set; }
        public string title { get; set; }
        [NonSerialized]
        public static int MaxWidth = 20;
    }
}