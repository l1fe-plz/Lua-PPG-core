using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;

namespace Lua{
    public class Lua : MonoBehaviour{
        //ядро мода, тут смешена отвественость это ок, оно и должно делать всё разом
        static string AbsoluteModPath = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(Application.dataPath, "..", ModAPI.Metadata.MetaLocation)));
        static string AbsoluteModsPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Mods"));
        static string AbsoluteSteamModsPath;
        private struct LuaCoroutine {
            public object CoroObj;
            public MoonSharpWrapper Wrapper;
        }
        private static List<LuaCoroutine> activeCoroutines = new List<LuaCoroutine>();
        private static Dictionary<object, float> coroutineTimers = new Dictionary<object, float>();
        private static List<MoonSharpWrapper> wrappers = new List<MoonSharpWrapper>();
        static List<LuaModMetadata> allMods = new List<LuaModMetadata>();
        static List<(string, LuaModMetadata, object)> scriptForCompilation;
        static string dllPath = Path.Combine(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", ModAPI.Metadata.MetaLocation)),
            "MoonSharp.Interpreter.dll"
        );
        private static Lua _instance;
        public static Lua Instance {
            get {
                if (_instance == null) {
                    var go = new GameObject("Lua");
                    _instance = go.AddComponent<Lua>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }
        public static string QwQтизация(string main) => main + "QwQ";
        public static Sprite NewSprite(string Path) => Utils.LoadSprite(Path, FilterMode.Point, 35f, false);
        public static Texture2D NewTexture(string Path) => Utils.LoadTexture(Path, FilterMode.Point, false);
        public static AudioClip NewSound(string Path) => Utils.FileToAudioClip(Path);

        public static void OnLoad(){
            print("[LUA] Start up!");
            string disk = Path.GetPathRoot(AbsoluteModsPath);
            string steamPath = @"SteamLibrary\steamapps\workshop\content\1118200";
            string combinedPath = Path.Combine(disk, steamPath);
            AbsoluteSteamModsPath = Path.GetFullPath(combinedPath);
            if (!Directory.Exists(AbsoluteSteamModsPath)) {
                Debug.LogError("[LUA FATAL] Opps we crash QwQ! Can't find steam workshop");
                return;
            }
            if(!UI.InitConsol(GameObject.Find("Canvas").GetComponent<Canvas>())){
                UI.Kill();
                Debug.LogError("[LUA FATAL] Opps we crash QwQ! Can't start UI");
                return;
            }
            _ = Instance;
            UI.DebugPrint("<color=#00a6ff>[LUA]</color> Core can start work...");
            UI.DebugPrint("<color=#00a6ff>[LUA]</color> Starting scan mods...");
            List<string> fileLocalPaths = Directory.GetFiles(AbsoluteModsPath, "lua.json", SearchOption.AllDirectories).ToList();
            List<string> fileSteamPaths = Directory.GetFiles(AbsoluteSteamModsPath, "lua.json", SearchOption.AllDirectories).ToList();
            fileLocalPaths.AddRange(fileSteamPaths);
            string[] filePaths = fileLocalPaths.ToArray();
            foreach (string path in filePaths)
            {
                try {
                    string jsonContent = File.ReadAllText(path);
                    LuaModMetadata data = JsonConvert.DeserializeObject<LuaModMetadata>(jsonContent);
                    if (data != null) {
                        data.ModPath = Path.GetDirectoryName(path);
                        allMods.Add(data);
                        UI.DebugPrint($"<color=#00a6ff>[LUA]</color> Loaded mod: <i>{data.Name}</i>");
                    }
                }
                catch (Exception e){
                    UI.DebugPrint($"<color=yellow>[LUA WARN] Invalid JSON: <i>{path}</i></color>");
                    UI.DebugPrint($"error: {e}");
                }
            }
            UI.DebugPrint("<color=#00a6ff>[LUA]</color> Scan completed!");

            UI.DebugPrint("<color=#00a6ff>[LUA]</color> Starting the compilation of scripts...");
            int errors = 0;
            scriptForCompilation = new List<(string, LuaModMetadata, object)>();

            foreach (var data in allMods){
                UI.DebugPrint($"<color=#00a6ff>[LUA]</color> {data.Name} initialised...");
                List<(string, LuaModMetadata, object)> scriptForCompilationFirst = new List<(string, LuaModMetadata, object)>();
                var ppgMod = ModLoader.LoadedMods.FirstOrDefault(m => 
                    Path.GetFullPath(m.MetaLocation) == Path.GetFullPath(data.ModPath));
                if (ppgMod != null) {
                    data.PPGModReference = ppgMod;
                    data.Active = ppgMod.Active;
                }
                else{
                    UI.DebugPrint($"<color=yellow>[LUA WARN] <i>{data.Name}</i> not contain mod.json</color>");
                    continue;
                }
                bool hasError = false;
                var wrapper = InitMoonSharp();
                data.MoonSharp = wrapper;
                string currentModPath = data.ModPath;

                wrapper.SetGlobal("NewTexture", (Func<string, Texture2D>)((relativePath) => {
                    string fullPath = Path.GetFullPath(Path.Combine(currentModPath, relativePath));
                    return NewTexture(fullPath);
                }));
                wrapper.SetGlobal("NewSprite", (Func<string, Sprite>)((relativePath) => {
                    string fullPath = Path.GetFullPath(Path.Combine(currentModPath, relativePath));
                    return NewSprite(fullPath);
                }));
                wrapper.SetGlobal("NewSound", (Func<string, AudioClip>)((relativePath) => {
                    string fullPath = Path.GetFullPath(Path.Combine(currentModPath, relativePath));
                    return NewSound(fullPath);
                }));
                wrapper.SetGlobal("AddLuaEvents", (Func<GameObject, LuaEvents>)((go) => {
                    return go.AddLuaEvents(wrapper);
                }));

                foreach (string script in data.Scripts){
                    string absoluteScriptPath = Path.Combine(data.ModPath, script);
                    if (!File.Exists(absoluteScriptPath)){
                        UI.DebugPrint($"<color=yellow>[LUA WARN] <i>{script}</i> doesn't exist, skip it (Mod: {data.Name})</color>");
                        continue;
                    }
                    try{
                        string scriptContent = File.ReadAllText(absoluteScriptPath);
                        object sсriptFunc = wrapper.LoadString(scriptContent, null, $"{data.Name}/{script}");
                        scriptForCompilationFirst.Add((absoluteScriptPath, data, sсriptFunc));
                    }
                    catch (Exception e){
                        Exception realError = e;
                        while (realError.InnerException != null) realError = realError.InnerException;
                        var decoratedProp = realError.GetType().GetProperty("DecoratedMessage");
                        string message = decoratedProp != null 
                            ? (decoratedProp.GetValue(realError)?.ToString() ?? realError.Message) 
                            : realError.Message;
                        
                        UI.DebugPrint($"<color=red>[LUA ERROR] {Path.GetFileName(absoluteScriptPath)}: {message}</color>");
                        errors++;
                        hasError = true;
                    }
                }
                if (hasError) {
                    scriptForCompilationFirst = null;
                    data.MoonSharp = null;
                    wrapper = null;
                    UI.DebugPrint($"<color=yellow>[LUA WARN] Could not compile mod <i>{data.Name}</i></color>");
                }
                else{
                    scriptForCompilation.AddRange(scriptForCompilationFirst);
                }
            }
            string c = errors > 0 ? "<color=red>" : "<color=green>";
            UI.DebugPrint($"<color=#00a6ff>[LUA]</color> End compilation, error count: {c}{errors}</color>");
            UI.DebugPrint($"<color=#00a6ff>[LUA]</color> Ready to work!");
            
            UI.DebugPrint("<color=#00a6ff>[LUA]</color> Start OnLoad()");
            foreach (var (path, metadata, compiledCode) in scriptForCompilation){
                if(metadata.PPGModReference.Active){
                    try{
                        metadata.MoonSharp.CallFunction(compiledCode);
                    }
                    catch (Exception e){
                        Exception realError = e;
                        while (realError.InnerException != null) realError = realError.InnerException;
                        
                        var decoratedProp = realError.GetType().GetProperty("DecoratedMessage");
                        string message = decoratedProp != null 
                            ? (decoratedProp.GetValue(realError)?.ToString() ?? realError.Message) 
                            : realError.Message;
                        
                        UI.DebugPrint($"<color=red>[LUA ERROR] {Path.GetFileName(path)}: {message}</color>");
                    }
                }
            }
        }
        void Update()
        {
            if(UI.TextConsol == null){
                    if(!UI.InitConsol(GameObject.Find("Canvas").GetComponent<Canvas>())){
                    UI.Kill();
                    Debug.LogError("[LUA FATAL] Opps we crash QwQ! Can't start UI");
                    Destroy(Instance);
                    return;
                }
            }
            if (Input.GetKeyDown(KeyCode.BackQuote))
            {
                UI.Consol();
            }
            foreach (var (path, metadata, compiledCode) in scriptForCompilation){
                if (metadata.PPGModReference.Active && !metadata.Active){
                    metadata.Active = true;
                    try{
                        metadata.MoonSharp.CallFunction(compiledCode);
                    }
                    catch (Exception e){
                        Exception realError = e;
                        while (realError.InnerException != null) realError = realError.InnerException;
                        
                        var decoratedProp = realError.GetType().GetProperty("DecoratedMessage");
                        string message = decoratedProp != null 
                            ? (decoratedProp.GetValue(realError)?.ToString() ?? realError.Message) 
                            : realError.Message;
                        
                        UI.DebugPrint($"<color=red>[LUA ERROR] {Path.GetFileName(path)}: {message}</color>");
                    }
                }
            }
            if (activeCoroutines == null || activeCoroutines.Count == 0) return;

            for (int i = activeCoroutines.Count - 1; i >= 0; i--)
            {
                var luaCoro = activeCoroutines[i];
                var coroObj = luaCoro.CoroObj;
                var coroWrapper = luaCoro.Wrapper;

                if (coroObj == null || coroWrapper == null) { 
                    activeCoroutines.RemoveAt(i); 
                    continue; 
                }
                if (coroutineTimers.ContainsKey(coroObj))
                {
                    coroutineTimers[coroObj] -= Time.deltaTime;
                    if (coroutineTimers[coroObj] > 0) continue;
                    else coroutineTimers.Remove(coroObj);
                }

                try
                {
                    var resumeMethod = coroObj.GetType().GetMethod("Resume", System.Type.EmptyTypes);
                    if (resumeMethod == null) continue;

                    object yieldResult = resumeMethod.Invoke(coroObj, null);
                    var stateProp = coroObj.GetType().GetProperty("State");
                    var state = stateProp?.GetValue(coroObj)?.ToString();

                    if (state == "Dead")
                    {
                        activeCoroutines.RemoveAt(i);
                        coroutineTimers.Remove(coroObj);
                    }
                    else
                    {
                        object waitValue = coroWrapper.FromDynValue(yieldResult);

                        if (waitValue is double || waitValue is float || waitValue is int)
                        {
                            float seconds = Convert.ToSingle(waitValue);
                            if (seconds > 0)
                            {
                                coroutineTimers[coroObj] = seconds;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    UI.DebugPrint($"<color=red>[LUA ERROR] {ex.InnerException?.Message ?? ex.Message}</color>");
                    activeCoroutines.RemoveAt(i);
                    coroutineTimers.Remove(coroObj);
                }
            }
        }
        public static void Main(){
            UI.DebugPrint("<color=#00a6ff>[LUA]</color> Start Main()");
            foreach (var mod in allMods)
            {
                if (mod.MoonSharp == null || !mod.PPGModReference.Active) continue;
                try
                {
                    object mainFunc = mod.MoonSharp.GetGlobal("Main");
                    if (mainFunc != null) {
                        mod.MoonSharp.CallFunction(mainFunc);
                    }
                }
                catch (Exception e)
                {
                    Exception realError = e;
                    while (realError.InnerException != null) realError = realError.InnerException;
                    
                    var decoratedProp = realError.GetType().GetProperty("DecoratedMessage");
                    string message = decoratedProp != null 
                        ? (decoratedProp.GetValue(realError)?.ToString() ?? realError.Message) 
                        : realError.Message;
                    
                    UI.DebugPrint($"<color=red>[LUA ERROR] {mod.Name} (Main): {message}</color>");
                }
            }
        }

        public static void OnUnload(){
            UI.DebugPrint("<color=#00a6ff>[LUA]</color> Start OnUnload()");
            foreach (var mod in allMods)
            {
                if (mod.MoonSharp == null || !mod.PPGModReference.Active) continue;
                try
                {
                    object unloadFunc = mod.MoonSharp.GetGlobal("OnUnload");
                    if (unloadFunc != null) {
                        mod.MoonSharp.CallFunction(unloadFunc);
                    }
                }
                catch (Exception e)
                {
                    Exception realError = e;
                    while (realError.InnerException != null) realError = realError.InnerException;
                    
                    var decoratedProp = realError.GetType().GetProperty("DecoratedMessage");
                    string message = decoratedProp != null 
                        ? (decoratedProp.GetValue(realError)?.ToString() ?? realError.Message) 
                        : realError.Message;
                    
                    UI.DebugPrint($"<color=red>[LUA ERROR] {mod.Name} (OnUnload): {message}</color>");
                }
            }
        }
        static MoonSharpWrapper InitMoonSharp(){
            var wrapper = new MoonSharpWrapper();
            if (!wrapper.Initialize(dllPath, true))
            {
                UI.DebugPrint("<color=red>[LUA ERROR] MoonSharp init failed</color>");
                return null;
            }
            else{
                wrapper.RegisterTypes(
                    typeof(ModAPI),
                    typeof(Modification),
                    typeof(SpawnableAsset),
                    typeof(Category),
                    typeof(PersonBehaviour),
                    typeof(LimbBehaviour),
                    typeof(Utils),
                    typeof(Sprite),
                    typeof(Texture2D),
                    typeof(Lua),
                    typeof(GameObject),
                    typeof(Transform),
                    typeof(Component),
                    typeof(SpriteRenderer),
                    typeof(Rigidbody2D), 
                    typeof(Collider2D),
                    typeof(MonoBehaviour),
                    typeof(Vector2),
                    typeof(Vector3),
                    typeof(Vector4),
                    typeof(Quaternion),
                    typeof(Matrix4x4),
                    typeof(Color),
                    typeof(Color32),
                    typeof(Mathf),
                    typeof(UnityEngine.Random),
                    typeof(Rect),
                    typeof(Bounds),
                    typeof(Action<GameObject>),
                    typeof(Action<object>),
                    typeof(Action),
                    typeof(System.Action),
                    typeof(System.Action<>),
                    typeof(System.Func<>),
                    typeof(System.Collections.Generic.List<>),
                    typeof(System.Collections.Generic.Dictionary<,>),
                    typeof(System.Array),
                    typeof(System.String),
                    typeof(System.Convert),
                    typeof(System.Enum),
                    typeof(Ray),
                    typeof(RaycastHit),
                    typeof(LayerMask),
                    typeof(Physics2D),
                    typeof(ContactPoint2D),
                    typeof(Collision2D),
                    typeof(LuaEvents),
                    typeof(Extensions)
                );
                wrapper.RegisterStandardFunctions();
                wrapper.SetGlobal("Action", (Func<object, object>)( (obj) => wrapper.ToAction(obj) ));
                wrapper.SetGlobal("startCoroutine", (Func<object, object>)((obj) => {
                    Instance.LuaStartCoroutine(obj, wrapper); 
                    return null;
                }));
                wrapper.SetGlobal("AddComponent", (Func<GameObject, object, Component>)((go, typeUserData) => {
                    Type targetType = wrapper.AsType(typeUserData);
                    
                    if (go == null || targetType == null) {
                        UI.DebugPrint("<color=red>[LUA ERROR] AddComponent: GameObject or Type is null</color>");
                        return null;
                    }
                    
                    return go.AddComponent(targetType);
                }));
                wrapper.SetGlobal("SetParent", (Action<Transform, object, bool>)((child, parentObj, worldStays) => {
                    if (child == null || parentObj == null) return;
                    Transform parent = wrapper.FromDynValue(parentObj) as Transform;
                    if (parent != null) {
                        child.SetParent(parent, worldStays);
                    } else {
                        UI.DebugPrint("<color=red>[LUA ERROR]</color> SetParent: Second argument is not a Transform!");
                    }
                }));
                wrapper.SetGlobal("New", (Func<object, object, object, object, object, object>)((typeArg, a, b, c, d) => {
                    object rawType = wrapper.FromDynValue(typeArg);
                    Type targetType = rawType as Type;

                    if (targetType == null) {
                        UI.DebugPrint($"<color=red>[LUA ERROR] New: First argument is not a Type! (Got {rawType?.GetType().Name ?? "nil"})</color>");
                        return null;
                    }
                    var argsList = new List<object>();
                    foreach (var arg in new[] { a, b, c, d }) {
                        if (arg == null) break;
                        if (arg is double || arg is float || arg is int)
                            argsList.Add(Convert.ToSingle(arg));
                        else
                            argsList.Add(arg);
                    }

                    try {
                        return Activator.CreateInstance(targetType, argsList.ToArray());
                    } catch (Exception ex) {
                        UI.DebugPrint($"<color=red>[LUA ERROR] Failed to create {targetType.Name}: {ex.Message}</color>");
                        return null;
                    }
                }));
                wrapper.RegisterType(typeof(UI));
                wrapper.SetGlobal("UI", wrapper.CreateStaticUserData(typeof(UI)));
                wrappers.Add(wrapper);
                return wrapper;
            }
        }
        public void LuaStartCoroutine(object func, MoonSharpWrapper currentWrapper)
        {
            var coro = currentWrapper.CreateCoroutine(func);
            if (coro != null) 
            {
                activeCoroutines.Add(new LuaCoroutine { CoroObj = coro, Wrapper = currentWrapper });
            }
        }
        public static Type GetUnityType(string name) {
            var type = Type.GetType(name) ?? typeof(UnityEngine.GameObject).Assembly.GetType("UnityEngine." + name);
            return type;
        }
        
    }
    public static class Extensions
    {
        public static LuaEvents AddLuaEvents(this GameObject instance, MoonSharpWrapper MoonSharp) {
            var existing = instance.GetComponent<LuaEvents>();
            LuaEvents a = null;
            if(!existing){ 
                a = instance.AddComponent<LuaEvents>();
                a.MoonSharp = MoonSharp;
            }
            return existing ?? a;
        }
        public static void ExecuteAll(this List<Action> sequence) {
            if (sequence == null || sequence.Count == 0) return;
            for (int i = 0; i < sequence.Count; i++) sequence[i]?.Invoke();
        }
        public static void ExecuteAll<T>(this List<Action<T>> sequence, T arg) {
            if (sequence == null || sequence.Count == 0) return;
            for (int i = 0; i < sequence.Count; i++) sequence[i]?.Invoke(arg);
        }
    }
    public class LuaEvents : MonoBehaviour{
        private List<Action> onAwake, onEnable, onDestroy, onDisable, onLevelWasLoaded, onApplicationPause, start, fixedUpdate, update, lateUpdate, slice;
        private List<Action<Collision2D>> onCollisionEnter2D, onCollisionExit2D, onCollisionStay2D;
        private List<Action<ActivationPropagation>> use, useContinuous;
        private List<Action<GripBehaviour>> onDrop, onGripped;
        private List<Action<Vector2>> onBreak;
        private List<Action<float>> damage;
        private List<Action<Shot>> shot;
        private List<Action<Stabbing>> stabbed, unstabbed;
        public MoonSharpWrapper MoonSharp;

        private Action<T> Bind<T>(object luaFunc) => (arg) => MoonSharp.CallFunction(luaFunc, arg);
        private Action Bind(object luaFunc) => () => MoonSharp.CallFunction(luaFunc);

        public void AddUpdate(object f) { (update ?? (update = new List<Action>())).Add(Bind(f)); this.enabled = true; }
        public void AddFixedUpdate(object f) { (fixedUpdate ?? (fixedUpdate = new List<Action>())).Add(Bind(f)); this.enabled = true; }
        public void AddLateUpdate(object f) { (lateUpdate ?? (lateUpdate = new List<Action>())).Add(Bind(f)); this.enabled = true; }
        
        public void AddStart(object f) => (start ?? (start = new List<Action>())).Add(Bind(f));
        public void AddAwake(object f) => (onAwake ?? (onAwake = new List<Action>())).Add(Bind(f));
        public void AddCollisionEnter(object f) => (onCollisionEnter2D ?? (onCollisionEnter2D = new List<Action<Collision2D>>())).Add(Bind<Collision2D>(f));
        public void AddCollisionExit(object f) => (onCollisionExit2D ?? (onCollisionExit2D = new List<Action<Collision2D>>())).Add(Bind<Collision2D>(f));
        
        public void AddUse(object f) => (use ?? (use = new List<Action<ActivationPropagation>>())).Add(Bind<ActivationPropagation>(f));
        public void AddShot(object f) => (shot ?? (shot = new List<Action<Shot>>())).Add(Bind<Shot>(f));
        public void AddDamage(object f) => (damage ?? (damage = new List<Action<float>>())).Add(Bind<float>(f));
        public void AddBreak(object f) => (onBreak ?? (onBreak = new List<Action<Vector2>>())).Add(Bind<Vector2>(f));
        public void AddStab(object f) => (stabbed ?? (stabbed = new List<Action<Stabbing>>())).Add(Bind<Stabbing>(f));

        void Awake() { if(update == null && fixedUpdate == null && lateUpdate == null) this.enabled = false; onAwake.ExecuteAll(); }

        void OnEnable() => onEnable.ExecuteAll();
        void OnDestroy() => onDestroy.ExecuteAll();
        void OnDisable() => onDisable.ExecuteAll();
        void Start() => start.ExecuteAll();
        
        void Update() => update.ExecuteAll();
        void FixedUpdate() => fixedUpdate.ExecuteAll();
        void LateUpdate() => lateUpdate.ExecuteAll();

        void OnCollisionEnter2D(Collision2D c) => onCollisionEnter2D.ExecuteAll(c);
        void OnCollisionExit2D(Collision2D c) => onCollisionExit2D.ExecuteAll(c);
        void OnCollisionStay2D(Collision2D c) => onCollisionStay2D.ExecuteAll(c);

        void Use(ActivationPropagation p) => use.ExecuteAll(p);
        void UseContinuous(ActivationPropagation p) => useContinuous.ExecuteAll(p);
        void OnDrop(GripBehaviour g) => onDrop.ExecuteAll(g);
        void OnGripped(GripBehaviour g) => onGripped.ExecuteAll(g);
        void Break(Vector2 v) => onBreak.ExecuteAll(v);
        void Damage(float d) => damage.ExecuteAll(d);
        void Slice() => slice.ExecuteAll();
        void Shot(Shot s) => shot.ExecuteAll(s);
        void Stabbed(Stabbing s) => stabbed.ExecuteAll(s);
        void Unstabbed(Stabbing s) => unstabbed.ExecuteAll(s);
    }

    [Serializable]
    public class LuaModMetadata{
        public string Name;
        public string[] Scripts;
        [HideInInspector] public string ModPath;
        [HideInInspector] public MoonSharpWrapper MoonSharp;
        [HideInInspector] public ModMetaData PPGModReference;
        [HideInInspector] public bool Active;
    }
}