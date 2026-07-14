namespace Resource.Scripts
{
    /// <summary>
    /// 跨场景记住"这局游戏是不是已经过了一次主菜单"。静态字段在同一次 Play 期间，
    /// 场景重新加载（重开关卡）或切到下一关都不会被重置，只有真正退出/重进 Play 模式才会清空。
    ///
    /// 用途：MainMenuUI 自举的时候会检查这个标记——如果玩家已经在主菜单选过一次关卡了，
    /// 之后不管是"重新开始"重载同一个场景，还是以后有多关卡时切到下一关的场景，
    /// 都不应该再弹一次主菜单，直接进游戏就行。等以后做"返回主菜单"功能时，
    /// 在那边把 HasEnteredGame 设回 false 即可。
    /// </summary>
    public static class GameFlowState
    {
        public static bool HasEnteredGame;
    }
}
