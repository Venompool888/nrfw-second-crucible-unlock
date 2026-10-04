namespace CrucibleUnlock
{
    public sealed class MotionQuat { public float x { get; set; } public float y { get; set; } public float z { get; set; } public float w { get; set; } }
    public sealed class MotionPresentationSnapshot
    {
        public string state { get; set; }
        public string error { get; set; }
        public MotionQuat root_rotation { get; set; }
        public MotionQuat visual_rotation { get; set; }
        public MotionQuat avatar_rotation { get; set; }
        public MotionQuat animator_rotation { get; set; }
        public MotionQuat skeleton_rotation { get; set; }
        public MotionQuat pelvis_local_rotation { get; set; }
        public MotionQuat pelvis_parent_rotation { get; set; }
        public long? pelvis_parent_identity { get; set; }
        public MotionVec3 avatar_lossy_scale { get; set; }
        public string leap_target_state { get; set; }
        public long? leap_target_identity { get; set; }
        public MotionVec3 leap_target_position { get; set; }
        public MotionVec3 leap_target_local_position { get; set; }
        public long? leap_target_parent_identity { get; set; }
        public MotionVec3 leap_target_parent_position { get; set; }
        public double? leap_target_distance_from_boss { get; set; }
        public long? camera_identity { get; set; }
        public string camera_state { get; set; }
        public MotionVec3 camera_position { get; set; }
        public MotionQuat camera_rotation { get; set; }
        public float? camera_fov { get; set; }
        public bool? camera_orthographic { get; set; }
        public float? camera_orthographic_size { get; set; }
        public bool? camera_zoom_active { get; set; }
        public long? camera_zoom_identity { get; set; }
        public float? camera_current_zoom { get; set; }
        public bool? camera_focus_active { get; set; }
        public long? camera_focus_identity { get; set; }
        public float? camera_focus_weight { get; set; }
        public long? camera_focus_point_identity { get; set; }
        public MotionVec3 camera_focus_point { get; set; }
        public bool? camera_cinematic_active { get; set; }
        public long? camera_cinematic_identity { get; set; }
        public string camera_ownership { get; set; }
    }

}
