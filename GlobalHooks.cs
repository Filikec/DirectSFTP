using SharpHook;

namespace DirectSFTP
{
    public static class GlobalHooks
    {
        public static TaskPoolGlobalHook hooks = new();
        public static void StartHooks()
        {
            hooks.RunAsync();
        }
    }
}
