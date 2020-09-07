using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using Network.Models;

namespace Network.Controllers
{
    public class NodesController : ApiController
    {
        //GET NODES WITHOUT COORDS
        [Route("api/nodes/exclude-coords")]
        [HttpGet]
        public List<VisNode> GetWithoutCoords()
        {
            List<VisNode> nodes = new List<VisNode>();
            Station.AllStations.ForEach(station =>
            {
                nodes.Add(new VisNode()
                {
                    id = station.ID,
                    label = station.Name,
                    color = VisNode.GetColor(station.Region),
                    image = station.GetImage(),
                    shape = "image",
                    title = $"Processing messages count: {station.ProcessingMessages.Count}<br> {station.AchievedMessages.Count} messages achieved : {station.AchievedMessagesIDs}"
                });
            });
            return nodes;
        }

        //GET NODES WITH COORDS
        [Route("api/nodes/include-coords")]
        [HttpGet]
        public List<VisNodeWithCoords> GetWithCoords()
        {
            List<VisNodeWithCoords> nodes = new List<VisNodeWithCoords>();
            Station.AllStations.ForEach(station =>
            {
                nodes.Add(new VisNodeWithCoords()
                {
                    id = station.ID,
                    label = station.Name,
                    color = VisNode.GetColor(station.Region),
                    image = station.GetImage(),
                    x = station.X,
                    y = station.Y,
                    shape = "image",
                    title = $"Packets in queue count: {station.ReceivedPackets.Count}<br> {station.AchievedMessages.Count} messages achieved : {station.AchievedMessagesIDs}"
                });
            });
            return nodes;
        }
        
        //GET NODE BY ID
        public Station Get(int id)
        {
            return Station.AllStations.FirstOrDefault(station => station.ID == id);
        }

        //SAVE NODES COORDS
        [Route("api/nodes/coords")]
        [HttpPost]
        public void Post([FromBody]List<VisNodeWithCoords> values)
        {
            if (Station.AllStations.Count != values.Count)
                return;

            values.ForEach(value =>
            {
                var station = Station.AllStations.FirstOrDefault(s => s.ID == value.id);
                station.X = value.x;
                station.Y = value.y;
            });
        }

        //CREATE NODES 
        [Route("api/nodes/create")]
        [HttpPost]
        // POST api/nodes
        public void Post([FromBody]List<Station> value)
        {
            value.ForEach(station =>
            {
                Station.AllStations.Add(station);
            });
            //station.Nodes.Add(value);
        }
        /*
        //UPDATE NODE BY ID
        public void Put(int id, [FromBody]Station value)
        {
            var station = Station.AllStations.FirstOrDefault(st => st.ID == id);
            if(value?.Nodes != null)
                station.Nodes = value.Nodes;
            if (value?.Region != null)
                station.Region = value.Region;
        }
        */
        // DELETE NODE BY ID
        public void Delete(int id)
        {
            //Maybe it's not importnant
            if (Message.AllMessages.Any(message => message.Status != "Completed"))
                return;

            var station = Station.AllStations.FirstOrDefault(s => s.ID == id);

            if (station != null)
            {
                Station.AllStations.Remove(station);
                station.Nodes.ForEach(node =>
                {
                    node.LinkedStation.Nodes.RemoveAll(n => n.LinkedStationID == station.ID);
                });
            }
        }
    }

    public class VisNodeWithCoords : VisNode
    {
        public int x { get; set; }
        public int y { get; set; }
    }

    public class VisNode
    {
        public int id { get; set; }
        public string label { get; set; }
        public string color { get; set; }
        public string image { get; set; }
        public string shape { get; set; }
        public string title { get; set; }
        public static string GetColor(int region)
        {
            switch (region)
            {
                case 1:
                    return "#AFBAE5";
                case 2:
                    return "#AFE5B6";
                case 3:
                    return "#FE8787";
                default:
                    return "#FEFA87";
            }
        }
    }
}
