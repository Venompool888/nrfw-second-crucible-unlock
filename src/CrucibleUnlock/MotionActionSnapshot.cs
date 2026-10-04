namespace CrucibleUnlock
{
    // A synchronous snapshot of current view animation objects. No wrapper is retained.
    public sealed class MotionActiveClipSnapshot
    {
        public int layer_index { get; set; }
        public long active_identity { get; set; }
        public long? animation_identity { get; set; }
        public float? crossfade_override { get; set; }
        public string crossfade_state { get; set; }
        public string crossfade_fault { get; set; }
        public bool current { get; set; }
        public string animation_name { get; set; }
        public string clip_name { get; set; }
        public float? time { get; set; }
        public float? weight { get; set; }
        public bool? playing { get; set; }
        public bool? stop_requested { get; set; }
        public bool? orphaned { get; set; }
        public float? simple_crossfade { get; set; }
        public float? layer_blend_in { get; set; }
        public float? layer_blend_out { get; set; }
        public bool? recenter_extraction { get; set; }
        public bool? keep_last_frame { get; set; }
        public string fault { get; set; }
    }

    public sealed class MotionActionSnapshot
    {
        public string base_action { get; set; }
        public string layer_action { get; set; }
        public MotionActiveClipSnapshot[] active_clips { get; set; }
        public bool truncated { get; set; }
        public int observed_active_count { get; set; }
        public int omitted_count { get; set; }
        public int consecutive_failures { get; set; }
        public string state { get; set; }
        public string fault { get; set; }
    }

}
