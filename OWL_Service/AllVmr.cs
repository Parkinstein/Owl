//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Manual changes to this file may cause unexpected behavior in your application.
//     Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace OWL_Service
{
    using System;
    using System.Collections.Generic;
    
    public partial class AllVmr
    {
        public int Id { get; set; }
        public Nullable<bool> allow_guests { get; set; }
        public string description { get; set; }
        public Nullable<bool> force_presenter_into_main { get; set; }
        public string guest_pin { get; set; }
        public string guest_view { get; set; }
        public string host_view { get; set; }
        public string max_callrate_in { get; set; }
        public string max_callrate_out { get; set; }
        public string name { get; set; }
        public string participant_limit { get; set; }
        public string pin { get; set; }
        public string resource_uri { get; set; }
        public string service_type { get; set; }
        public string tag { get; set; }
        public Nullable<int> vmid { get; set; }
    }
}
