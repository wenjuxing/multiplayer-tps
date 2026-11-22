// 保存当前角色的走跑速度配置和状态选出的移动速度。
public class PlayerContext {
    // 由移动组件提供当帧使用的走跑速度。
    internal float WalkSpeed { get; set; }
    internal float RunSpeed { get; set; }

    // 由当前状态选择速度，移动组件随后应用前后方向。
    internal float MoveSpeed { get; set; }
}
