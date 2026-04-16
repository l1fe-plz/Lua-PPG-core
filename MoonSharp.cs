using UnityEngine;
using System;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;

namespace Lua
{
    public class MoonSharpWrapper
    {
        private static readonly HashSet<string> BannedTypes = new HashSet<string>{
            D("SHR0cFdlYlJlcXVlc3Q="), D("V2ViQ2xpZW50"), D("SHR0cENsaWVudA=="), D("VW5pdHlXZWJSZXF1ZXN0"),
            D("U29ja2V0"), D("VGNwQ2xpZW50"), D("VWRwQ2xpZW50"),
            D("QXNzZW1ibHk="), D("QXNzZW1ibHlCdWlsZGVy"), D("TWV0aG9kSW5mbw=="), D("RmllbGRJbmZv"), D("UHJvcGVydHlJbmZv"),
            D("Q29uc3RydWN0b3JJbmZv"), D("VHlwZUluZm8="), D("QWN0aXZhdG9y"), D("RHluYW1pY01ldGhvZA=="), D("TWV0aG9kUmVudGFs"),
            D("SGFybW9ueQ=="), D("SGFybW9ueUxpYg=="), D("UGF0Y2hGdW5jdGlvbnM="), D("RGV0b3Vy"),
            D("Q1NoYXJwQ29kZVByb3ZpZGVy"), D("VkJDb2RlUHJvdmlkZXI="), D("Q29kZURvbVByb3ZpZGVy"),
            D("RmlsZVN5c3RlbVdhdGNoZXI="), D("UmVnaXN0cnk="), D("UmVnaXN0cnlLZXk="), D("U2VydmljZUNvbnRyb2xsZXI="),
            D("REVTQ3J5cHRvU2VydmljZVByb3ZpZGVy"), D("UmlqbmRhZWxNYW5hZ2Vk"), D("UlNBQ3J5cHRvU2VydmljZVByb3ZpZGVy"),
            D("TmFtZWRQaXBlU2VydmVyU3RyZWFt"), D("TXV0ZXg="), D("U2VtYXBob3Jl"), D("Q2xpcGJvYXJk"), D("RGF0YU9iamVjdA=="),
            D("T2xlRGJDb25uZWN0aW9u"), D("U3FsQ29ubmVjdGlvbg=="), D("WERvY3VtZW50"), D("WG1sRG9jdW1lbnQ="), D("WFBhdGhEb2N1bWVudA=="),
            D("VW5tYW5hZ2VkTWVtb3J5U3RyZWFt"), D("U2FmZUhhbmRsZQ=="), D("Q3JpdGljYWxGaW5hbGl6ZXJPYmplY3Q=")
        };
        private static string D(string b64) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));
        private Assembly assembly;
        private object scriptInstance;
        public Type scriptType;
        public Type dynValueType;
        public Type tableType;
        public Type userDataType;
        public Type closureType;

        private MethodInfo doStringMethod;
        private MethodInfo loadStringMethod;
        private MethodInfo runFileMethod;
        private MethodInfo fromObjectMethod;
        private MethodInfo registerTypeMethod;

        private PropertyInfo globalsProperty;
        private object globalsObject;
        private bool isInitialized = false;

        private void print(object obj) => Debug.Log(obj);

        public bool Initialize(string dllPath, bool enableDebug = true)
        {
            try
            {
                print($"[MoonSharp] Starting initialization from: {dllPath}");
                if (!LoadAssembly(dllPath)) 
                {
                    print("[MoonSharp Fatal] Failed to load assembly");
                    return false;
                }
                if (!GetTypes()) 
                {
                    print("[MoonSharp Fatal] Failed to extract internal types");
                    return false;
                }
                if (!CreateScript()) 
                {
                    print("[MoonSharp Fatal] Failed to instantiate Script object");
                    return false;
                }
                if (!ConfigureOptions(enableDebug)) 
                {
                    print("[MoonSharp Fatal Failed to configure debug options");
                    return false;
                }
                if (!GetMethods()) 
                {
                    print("[MoonSharp Fatal] Failed to map internal methods via reflection");
                    return false;
                }
                if (!GetGlobals()) 
                {
                    print("[MoonSharp Fatal] Failed to access Globals table");
                    return false;
                }

                isInitialized = true;
                print("[MoonSharp] Initialization successful");
                return true;
            }
            catch (Exception ex)
            {
                print($"[MoonSharp Fatal] Critical Init error: {ex}");
                return false;
            }
        }

        public object LoadString(string code, object globalTable = null, string friendlyName = null)
        {
            EnsureInitialized();
            object table = globalTable ?? GetGlobalsTable();
            string name = friendlyName ?? "chunk";
            return loadStringMethod.Invoke(scriptInstance, new object[] { code, table, name });
        }

        public object DoString(string code, object globalTable = null, string friendlyName = null)
        {
            EnsureInitialized();
            object table = globalTable ?? GetGlobalsTable();
            string name = friendlyName ?? "chunk";
            var result = doStringMethod.Invoke(scriptInstance, new object[] { code, table, name });
            return result;
        }

        public object RunFile(string filePath, object globalContext = null, string friendlyName = null)
        {
            EnsureInitialized();

            if (runFileMethod.GetParameters().Length == 1)
            {
                return runFileMethod.Invoke(scriptInstance, new object[] { filePath });
            }

            object context = globalContext ?? GetGlobalsTable();
            string name = friendlyName ?? Path.GetFileName(filePath);
            return runFileMethod.Invoke(scriptInstance, new object[] { filePath, context, name });
        }

        public object Globals => globalsObject;

        private object GetGlobalsTable()
        {
            var table = globalsProperty.GetValue(scriptInstance);
            return table;
        }

        public void RegisterStandardFunctions()
        {
            EnsureInitialized();

            var tokenType = assembly.GetType("MoonSharp.Interpreter.CoroutineStopToken");
            
            if (tokenType != null)
            {
                var yieldToken = tokenType.GetField("Yield", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

                this.SetGlobal("wait", (Func<double, object>)(seconds => 
                {
                    return yieldToken; 
                }));
            }
            else
            {
                print("[MoonSharp Warn] CoroutineStopToken not found, 'wait' might not work");
            }
        }
        public Type AsType(object obj)
        {
            if (obj == null) return null;
            if (obj is Type t) return t;
            
            var result = FromDynValue(obj);
            if (result is Type targetType) return targetType;

            return null;
        }
        public void SetGlobal(string name, object value)
        {
            EnsureInitialized();

            object dynValue = (value?.GetType() == dynValueType) ? value : FromObject(value);
            var setMethod = globalsObject.GetType().GetMethod("Set", new[] { typeof(object), dynValueType });

            if (setMethod == null)
            {
                print("[MoonSharp Fatal] Table.Set(object, DynValue) not found!");
                throw new Exception("MoonSharp Table.Set method not found");
            }

            setMethod.Invoke(globalsObject, new object[] { name, dynValue });
            print($"[MoonSharp] Global '{name}' assigned successfully");
        }

        public object GetGlobal(string name)
        {
            EnsureInitialized();
            var getMethod = globalsObject.GetType().GetMethod("Get", new[] { typeof(string) });
            var dynValue = getMethod?.Invoke(globalsObject, new object[] { name });
            var result = FromDynValue(dynValue);
            return result;
        }

        public object CreateCoroutine(object function)
        {
            var closure = ExtractClosure(function);
            if (closure == null) 
            {
                print("[MoonSharp Error] Failed to extract closure for coroutine");
                return null;
            }

            var createCoroutineMethod = scriptType.GetMethod("CreateCoroutine", new Type[] { closureType });
            var dynValueCoro = createCoroutineMethod?.Invoke(scriptInstance, new object[] { closure });

            if (dynValueCoro != null)
            {
                var coro = dynValueCoro.GetType().GetProperty("Coroutine")?.GetValue(dynValueCoro);
                return coro;
            }
            return null;
        }

        public object ResumeCoroutine(object coro) 
        {
            if (coro == null) return null;
            var resumeMethod = coro.GetType().GetMethod("Resume");
            var result = resumeMethod?.Invoke(coro, null);
            return result;
        }

        public bool IsCoroutineDead(object coro) 
        {
            if (coro == null) return true;
            var stateProp = coro.GetType().GetProperty("State");
            if (stateProp == null) return true;
            
            string state = stateProp.GetValue(coro).ToString();
            return state == "Dead";
        }

        public object CreateAction<T>(object luaFunction)
        {
            if (luaFunction == null) return null;
            Action<T> action = (arg) => 
            {
                CallFunction(luaFunction, arg);
            };
            return action;
        }

        public object CreateGameObjectAction(object luaFunction)
        {
            return CreateAction<GameObject>(luaFunction);
        }
        public void RegisterType(Type type, bool setGlobal = true)
        {
            EnsureInitialized();
            
            var methods = userDataType.GetMethods()
                .Where(m => m.Name == "RegisterType" && m.IsStatic)
                .ToArray();

            var overload = methods.FirstOrDefault(m =>
            {
                var p = m.GetParameters();
                return p.Length >= 1 && p[0].ParameterType == typeof(Type);
            });

            if (overload == null) return;

            var parameters = overload.GetParameters();
            var args = new object[parameters.Length];
            args[0] = type;

            for (int i = 1; i < parameters.Length; i++)
            {
                var pt = parameters[i].ParameterType;
                if (pt.IsEnum) args[i] = Enum.GetValues(pt).GetValue(0);
                else if (pt == typeof(string)) args[i] = type.Name;
                else args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            }

            overload.Invoke(null, args);
            if (setGlobal) {
                SetGlobal(type.Name, CreateStaticUserData(type));
            }
        }

        public void RegisterTypes(params Type[] types)
        {
            foreach (var type in types)
                RegisterType(type, true);
        }

        public object CreateStaticUserData(Type type)
        {
            var createStaticMethod = userDataType.GetMethods()
                .FirstOrDefault(m =>
                    m.Name == "CreateStatic" &&
                    m.IsStatic &&
                    m.GetParameters().Length >= 1 &&
                    m.GetParameters()[0].ParameterType == typeof(Type));

            if (createStaticMethod == null) throw new Exception("MoonSharp CreateStatic overload not found");

            var parameters = createStaticMethod.GetParameters();
            var args = new object[parameters.Length];
            args[0] = type;

            for (int i = 1; i < args.Length; i++)
            {
                if (parameters[i].ParameterType.IsEnum)
                    args[i] = Enum.GetValues(parameters[i].ParameterType).GetValue(0);
                else
                    args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            }

            return createStaticMethod.Invoke(null, args);
        }

        public void RegisterType<T>()
        {
            RegisterType(typeof(T), true);
        }

        public object FromObject(object obj)
        {
            EnsureInitialized();
            return fromObjectMethod.Invoke(null, new object[] { scriptInstance, obj });
        }

        public object ToObject(object dynValue)
        {
            return FromDynValue(dynValue);
        }

        public Action<GameObject> ToAction(object luaFunction)
        {
            if (luaFunction == null) return null;
            return (GameObject go) => CallFunction(luaFunction, go);
        }

        public object CallFunction(object luaFunction, params object[] args)
        {
            EnsureInitialized();
            if (luaFunction == null) return null;

            var callMethod = scriptType.GetMethod("Call", new[] { typeof(object), typeof(object[]) });
            if (callMethod != null)
            {
                var result = callMethod.Invoke(scriptInstance, new object[] { luaFunction, args });
                return result;
            }
            
            print("[MoonSharp Error] Script.Call(object, object[]) method not found");
            return null;
        }

        private void EnsureInitialized()
        {
            if (!isInitialized)
            {
                print("[MoonSharp Fatal] Attempted to use wrapper before initialization!");
                throw new Exception("Not initialized");
            }
        }

        private bool LoadAssembly(string dllPath)
        {
            assembly = Assembly.LoadFrom(dllPath);
            return assembly != null;
        }

        private bool GetTypes()
        {
            scriptType = assembly.GetType("MoonSharp.Interpreter.Script");
            dynValueType = assembly.GetType("MoonSharp.Interpreter.DynValue");
            tableType = assembly.GetType("MoonSharp.Interpreter.Table");
            userDataType = assembly.GetType("MoonSharp.Interpreter.UserData");
            closureType = assembly.GetType("MoonSharp.Interpreter.Closure");

            return scriptType != null && dynValueType != null && userDataType != null;
        }

        private bool CreateScript()
        {
            scriptInstance = Activator.CreateInstance(scriptType);
            return scriptInstance != null;
        }

        private bool ConfigureOptions(bool enableDebug)
        {
            if (!enableDebug) return true;

            var optionsProp = scriptType.GetProperty("Options");
            var options = optionsProp?.GetValue(scriptInstance);
            var debugPrintProp = options?.GetType().GetProperty("DebugPrint");

            if (debugPrintProp != null)
            {
                Action<string> logger = (msg) => {
                    UI.DebugPrint($"<color=#0080ff>[LUA SCRIPT]</color> {msg ?? "nil"}");
                };
                debugPrintProp.SetValue(options, logger);
            }

            return true;
        }

        private bool GetMethods()
        {
            loadStringMethod = scriptType.GetMethods().FirstOrDefault(m => {
                var p = m.GetParameters();
                return m.Name == "LoadString" && p.Length == 3 && p[1].ParameterType.Name == "Table";
            });

            doStringMethod = scriptType.GetMethods().FirstOrDefault(m => {
                var p = m.GetParameters();
                return m.Name == "DoString" && p.Length == 3 && p[1].ParameterType.Name == "Table";
            });

            runFileMethod = scriptType.GetMethods().FirstOrDefault(m => {
                var p = m.GetParameters();
                return m.Name == "RunFile" && p.Length == 3 && p[1].ParameterType.Name == "Table";
            });

            fromObjectMethod = dynValueType.GetMethods().FirstOrDefault(m =>
                m.Name == "FromObject" && m.IsStatic && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType.Name == "Script");

            return loadStringMethod != null && doStringMethod != null && fromObjectMethod != null;
        }

        private bool GetGlobals()
        {
            globalsProperty = scriptType.GetProperty("Globals");
            globalsObject = globalsProperty?.GetValue(scriptInstance);
            return globalsObject != null;
        }

        private object ExtractClosure(object obj)
        {
            if (obj == null) return null;
            if (closureType != null && obj.GetType() == closureType) return obj;

            var typeProp = obj.GetType().GetProperty("Type");
            if (typeProp != null && typeProp.GetValue(obj).ToString() == "Function")
            {
                return obj.GetType().GetProperty("Function")?.GetValue(obj) 
                    ?? obj.GetType().GetProperty("Closure")?.GetValue(obj);
            }

            var closureProp = obj.GetType().GetProperty("Closure");
            return closureProp?.GetValue(obj);
        }

        public object FromDynValue(object dynValue)
        {
            if (dynValue == null) return null;

            var typeProp = dynValue.GetType().GetProperty("Type");
            if (typeProp == null) return dynValue;

            string typeName = typeProp.GetValue(dynValue)?.ToString();

            switch (typeName)
            {
                case "Nil": return null;
                case "Number": return dynValue.GetType().GetProperty("Number")?.GetValue(dynValue);
                case "String": return dynValue.GetType().GetProperty("String")?.GetValue(dynValue);
                case "Boolean": return dynValue.GetType().GetProperty("Boolean")?.GetValue(dynValue);
                case "Table": return dynValue.GetType().GetProperty("Table")?.GetValue(dynValue);
                case "UserData": 
                    var udProperty = dynValue.GetType().GetProperty("UserData");
                    var userDataObj = udProperty?.GetValue(dynValue);
                    var result = userDataObj?.GetType().GetProperty("Object")?.GetValue(userDataObj);
                    return result;
                default: return dynValue;
            }
        }
        private static bool IsNotBanned(Type type) => !BannedTypes.Contains(type.Name);

        private static bool gameTypesRegistered = false;
        private static Dictionary<string, Type> typeCache = new Dictionary<string, Type>();

        public void RegisterAllGameTypes() {
            if (gameTypesRegistered) return;

            string managedPath = Path.Combine(Application.dataPath, "Managed");
            string[] targetDlls = { 
                D("QXNzZW1ibHktQ3NoYXJwLmRsbA=="),
                D("VW5pdHlFbmdpbmUuQ29yZU1vZHVsZS5kbGw="),
                D("VW5pdHlFbmdpbmUuUGh5c2ljczJETW9kdWxlLmRsbA=="),
                D("VW5pdHlFbmdpbmUuVUkuZGxs")
            };
            
            foreach (string dllName in targetDlls) {
                try {
                    string fullPath = Path.Combine(managedPath, dllName);
                    if (File.Exists(fullPath)) {
                        var asm = Assembly.LoadFrom(fullPath);
                        var types = asm.GetTypes()
                            .Where(t => t.IsPublic && (!t.IsAbstract || (t.IsAbstract && t.IsSealed)) && !t.IsGenericType)
                            .Where(IsNotBanned);

                        foreach (var type in types) {
                            RegisterType(type, false); 
                            if (!typeCache.ContainsKey(type.Name)) {
                                typeCache[type.Name] = type;
                            }
                        }
                    }
                } catch (Exception e) {
                    UI.DebugPrint($"[LUA Error] Assemb {dllName} don't load: {e.Message}");
                }
            }
            gameTypesRegistered = true;
        }
        public object GetStaticType(string className) {
            if (typeCache.TryGetValue(className, out Type t)) {
                return CreateStaticUserData(t);
            }
            return null;
        }
    }
}