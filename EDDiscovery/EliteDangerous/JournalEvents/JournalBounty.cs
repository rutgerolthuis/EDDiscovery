﻿using Newtonsoft.Json.Linq;
using System.Linq;

namespace EDDiscovery.EliteDangerous.JournalEvents
{
    //When written: player is awarded a bounty for a kill
    //Parameters: 
    //•	Faction: the faction awarding the bounty
    //•	Reward: the reward value
    //•	VictimFaction: the victim’s faction
    //•	SharedWithOthers: whether shared with other players
    public class JournalBounty : JournalEntry
    {
        public JournalBounty(JObject evt, EDJournalReader reader) : base(evt, JournalTypeEnum.Bounty,  reader)
        {
            Faction = evt.Value<string>("Faction");
            Reward = evt.Value<int>("Reward");
            VictimFaction = evt.Value<string>("VictimFaction");
            SharedWithOthers = evt.Value<bool?>("SharedWithOthers");

        }
        public string Faction { get; set; }
        public int Reward { get; set; }
        public string VictimFaction { get; set; }
        public bool? SharedWithOthers { get; set; }
    }
}