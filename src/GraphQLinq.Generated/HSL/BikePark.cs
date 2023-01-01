namespace HSL
{
    using System;
    using System.Collections.Generic;
    using GraphQLinq.Attributes;
    using System.Reflection;

    [GraphQL(Name = "BikePark")]
    public partial class BikePark : Node, PlaceInterface
    {
        [GraphQL(Name = "id")]
        public string Id { get; set; }
        [GraphQL(Name = "bikeParkId")]
        public string BikeParkId { get; set; }
        [GraphQL(Name = "name")]
        public string Name { get; set; }
        [GraphQL(Name = "spacesAvailable")]
        public int? SpacesAvailable { get; set; }
        [GraphQL(Name = "realtime")]
        public bool? Realtime { get; set; }
        [GraphQL(Name = "lon")]
        public float? Lon { get; set; }
        [GraphQL(Name = "lat")]
        public float? Lat { get; set; }
        [GraphQL(Name = "tags")]
        public List<string> Tags { get; set; }
        [GraphQL(Name = "openingHours")]
        public List<LocalTimeSpanDate> OpeningHours { get; set; }
    }
}