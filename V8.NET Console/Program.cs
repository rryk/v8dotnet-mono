﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using V8.Net;

namespace V8.Net
{
    public class Program
    {
        static V8Engine _JSServer;

        static Timer _TitleUpdateTimer;

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.FirstChanceException += CurrentDomain_FirstChanceException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            try
            {
                Console.Title = "V8.Net Console (" + (IntPtr.Size == 4 ? "32-bit" : "64-bit") + " mode)";

                Console.Write(Environment.NewLine + "Creating a V8Engine instance ...");

                _JSServer = new V8Engine();

                Console.WriteLine(Environment.NewLine + "... Done!");

                _TitleUpdateTimer = new Timer(500);
                _TitleUpdateTimer.AutoReset = true;
                _TitleUpdateTimer.Elapsed += (_o, _e) =>
                {
                    Console.Title = "V8.Net Console (Handles: " + _JSServer.TotalHandles
                        + " / Pending Native GC: " + _JSServer.TotalHandlesPendingDisposal
                        + " / Cached: " + _JSServer.TotalHandlesCached
                        + " / In Use: " + (_JSServer.TotalHandles - _JSServer.TotalHandlesCached) + ")";
                };
                _TitleUpdateTimer.Start();

                _JSServer.WithContextScope = () =>
                {
                    Console.WriteLine(Environment.NewLine + "Creating a global 'dump(obj)' function to dump properties of objects (one level only) ...");
                    _JSServer.ConsoleExecute(@"dump = function(o) { var s=''; if (typeof(o)=='undefined') return 'undefined'; for (var p in o) s+='* '+(o.valueOf())+'.'+p+' = ('+o[p]+')\r\n'; return s; }");

                    Console.WriteLine(Environment.NewLine + "Creating a global 'assert(a,b,msg)' function for property value assertion ...");
                    _JSServer.ConsoleExecute(@"assert = function(msg,a,b) { msg += ' ('+a+'==='+b+'?)'; if (a === b) return msg+' ... Ok.'; else throw msg+' ... Failed!'; }");

                    Console.WriteLine(Environment.NewLine + "Creating a global 'Console' object with a 'WriteLine' function ...");
                    _JSServer.CreateObject<JS_Console>();

                    Console.WriteLine(Environment.NewLine + "Creating a new global type 'WrappableObject' as 'Wrappable_Object' ...");
                    _JSServer.GlobalObject.SetProperty(typeof(WrappableObject), null, true, V8PropertyAttributes.Locked);

                    Console.WriteLine(Environment.NewLine + "Creating a new wrapped and locked object 'wrappedObject' ...");
                    _JSServer.GlobalObject.SetProperty("wrappedObject", new WrappableObject(), true, V8PropertyAttributes.Locked);
                };

                Console.WriteLine(Environment.NewLine + Environment.NewLine + @"Ready - just enter script to execute. Type '\' or '\help' for a list of console specific commands.");

                //// Test for http://v8dotnet.codeplex.com/discussions/447755#post1065482
                //{
                //    const string ShortJSON = "{\"result\":true,\"count\":3}";

                //    const string JavaScriptTemplate =
                //        "var json = '" + ShortJSON + "'; var obj = {};";/* +
                //        "var obj = JSON.parse(json);";*/

                //    //var javaScriptShortJSON = string.Format(JavaScriptTemplate, ShortJSON);

                //    var jsEngine = _JSServer;

                //    jsEngine.WithContextScope = () =>
                //    {
                //        jsEngine.ConsoleExecute(JavaScriptTemplate);
                //        InternalHandle resultHandle = jsEngine.DynamicGlobalObject.obj;
                //        var obj = jsEngine.GetObject(resultHandle);
                //        Console.WriteLine("Test ok.");
                //    };
                //}

                string input, lcInput;

                while (true)
                {
                    try
                    {
                        Console.Write(Environment.NewLine + "> ");

                        input = Console.ReadLine();
                        lcInput = input.Trim().ToLower();

                        if (lcInput == @"\help" || lcInput == @"\")
                        {
                            Console.WriteLine(@"Special console commands (all commands are triggered via a preceding '\' character so as not to confuse it with script code):");
                            Console.WriteLine(@"\cls - Clears the screen.");
                            Console.WriteLine(@"\test - Starts the test process.");
                            Console.WriteLine(@"\gc - Triggers garbage collection (for testing purposes).");
                            Console.WriteLine(@"\v8gc - Triggers garbage collection in V8 (for testing purposes).");
                            Console.WriteLine(@"\gctest - Runs a simple test against V8.NET and the native V8 engine.");
                            Console.WriteLine(@"\speedtest - Runs a simple test script to test V8.NET integration with the V8 engine.");
                            Console.WriteLine(@"\exit - Exists the console.");
                        }
                        else if (lcInput == @"\cls")
                            Console.Clear();
                        else if (lcInput == @"\test")
                        {
                            try
                            {
                                /* This command will serve as a means to run fast tests against various aspects of V8.NET from the JavaScript side.
                                 * This is preferred over unit tests because 1. it takes a bit of time for the engine to initialize, 2. internal feedback
                                 * can be sent to the console from the environment, and 3. serves as a nice implementation example.
                                 * The unit testing project will serve to test basic engine instantiation and solo utility classes.
                                 * In the future, the following testing process may be redesigned to be runnable in both unit tests and console apps.
                                 */

                                Console.WriteLine("\r\n===============================================================================");
                                Console.WriteLine("Setting up the test environment ...\r\n");


                                _JSServer.WithContextScope = () =>
                                {
                                    {
                                        // ... create a function template in order to generate our object! ...
                                        // (note: this is not using ObjectTemplate because the native V8 does not support class names for those objects [class names are object type names])

                                        Console.Write("\r\nCreating a FunctionTemplate instance ...");
                                        var funcTemplate = _JSServer.CreateFunctionTemplate(typeof(V8DotNetTesterWrapper).Name);
                                        Console.WriteLine(" Ok.");

                                        // ... use the template to generate our object ...

                                        Console.Write("\r\nRegistering the custom V8DotNetTester function object ...");
                                        var testerFunc = funcTemplate.GetFunctionObject<V8DotNetTesterFunction>();
                                        _JSServer.DynamicGlobalObject.V8DotNetTesterWrapper = testerFunc;
                                        Console.WriteLine(" Ok.  'V8DotNetTester' is now a type [Function] in the global scope.");

                                        Console.Write("\r\nCreating a V8DotNetTester instance from within JavaScript ...");
                                        // (note: Once 'V8DotNetTester' is constructed, the 'Initialize()' override will be called immediately before returning,
                                        // but you can return "engine.GetObject<V8DotNetTester>(_this.Handle, true, false)" to prevent it.)
                                        _JSServer.ConsoleExecute("testWrapper = new V8DotNetTesterWrapper();");
                                        _JSServer.ConsoleExecute("tester = testWrapper.tester;");
                                        Console.WriteLine(" Ok.");

                                        // ... Ok, the object exists, BUT, it is STILL not yet part of the global object, so we add it next ...

                                        Console.Write("\r\nRetrieving the 'tester' property on the global object for the V8DotNetTester instance ...");
                                        var handle = _JSServer.GlobalObject.GetProperty("tester");
                                        var tester = (V8DotNetTester)_JSServer.DynamicGlobalObject.tester;
                                        Console.WriteLine(" Ok.");

                                        Console.WriteLine("\r\n===============================================================================");
                                        Console.WriteLine("Dumping global properties ...\r\n");

                                        _JSServer.ConsoleExecute("dump(this)");

                                        Console.WriteLine("\r\n===============================================================================");
                                        Console.WriteLine("Dumping tester properties ...\r\n");

                                        _JSServer.ConsoleExecute("dump(tester)");

                                        // ... example of adding a functions via script (note: V8Engine.GlobalObject.Properties will have 'Test' set) ...

                                        Console.WriteLine("\r\n===============================================================================");
                                        Console.WriteLine("Ready to run the tester, press any key to proceed ...\r\n");
                                        Console.ReadKey();


                                        tester.Execute();

                                        //Console.WriteLine("\r\n===============================================================================\r\n");
                                        //Console.WriteLine("Testing garbage collection: Setting 'this.tempObj' to a new managed ...");

                                        //tempObj = JSServer.CreateObject();
                                        //tempObjID = tempObj.ID;
                                        //JSServer.DynamicGlobalObject.tempObj = tempObj;
                                    }

                                    //Console.WriteLine("Triggering the garbage collection ...");

                                    //GC.Collect();
                                    //GC.WaitForPendingFinalizers();

                                    //Console.WriteLine("Looking for object ...");

                                    //var obj = JSServer.GetObjectByID(tempObjID);

                                    //if (obj != null) throw new Exception("Object was not garbage collected.");

                                    Console.WriteLine("\r\n===============================================================================\r\n");
                                    Console.WriteLine("Test completed successfully! Any errors would have interrupted execution.");
                                    Console.WriteLine("Note: The 'dump(obj)' function is still available to use for manual testing.");
                                };
                            }
                            catch
                            {
                                Console.WriteLine("\r\nTest failed.\r\n");
                                throw;
                            }
                        }
                        else if (lcInput == @"\gc")
                        {
                            Console.Write("\r\nForcing garbage collection ... ");
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                            Console.WriteLine("Done.\r\n");
                        }
                        else if (lcInput == @"\v8gc")
                        {
                            Console.Write("\r\nForcing V8 garbage collection ... ");
                            _JSServer.WithContextScope = () =>
                            {
                                _JSServer.ForceV8GarbageCollection();
                            };
                            Console.WriteLine("Done.\r\n");
                        }
                        else if (lcInput == @"\gctest")
                        {
                            Console.WriteLine("\r\nTesting garbage collection ... ");

                            V8NativeObject tempObj;
                            int tempObjID;
                            int tempHandleID;
                            InternalHandle internalHandle = InternalHandle.Empty;

                            _JSServer.WithContextScope = () =>
                            {
                                Console.WriteLine("Setting 'this.tempObj' to a new managed object ...");

                                tempObj = _JSServer.CreateObject();
                                tempObjID = tempObj.ID;
                                internalHandle = tempObj.Handle;
                                Handle testHandle = internalHandle;
                                tempHandleID = testHandle.ID;
                                _JSServer.DynamicGlobalObject.tempObj = tempObj;

                                // ... because we have a strong reference to the handle, the managed and native objects are safe; however,
                                // this block has the only strong reference, so once the reference goes out of scope, the managed GC will attempt to
                                // collect it, which will mark the handle as ready for collection (but it will not be destroyed just yet) ...

                                Console.WriteLine("Clearing managed references and running the garbage collector ...");
                                testHandle = null;

                            };

                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                            // (we wait for the 'testHandle' handle object to be collected, which will dispose the handle)
                            // (note: we do not call 'Set()' on 'internalHandle' because the "Handle" type takes care of the disposal)

                            if (internalHandle.ReferenceCount > 1)
                                throw new Exception("Handle is still not ready for GC ... something is wrong.");

                            Console.WriteLine("Success! The managed handle instance is pending disposal.");
                            Console.WriteLine("Clearing the handle object reference next ...");

                            // ... because we still have a reference to 'tempObj' at this point, the managed and native objects are safe (and the handle
                            // above!); however, this block has the only strong reference keeping everything alive (including the handle), so once the
                            // reference goes out of scope, the managed GC will collect it, which will mark the managed object as ready for collection.
                            // Once both the managed object and handle are marked, this in turn marks the native handle as weak. When the native V8
                            // engine's garbage collector is ready to dispose of the handle, as call back is triggered and the the native object and
                            // handles will finally be removed ...

                            tempObj = null;

                            GC.Collect();
                            GC.WaitForPendingFinalizers();

                            Console.WriteLine("Looking for object ...");

                            if (!internalHandle.IsDisposed) throw new Exception("Managed object was not garbage collected.");
                            // (note: this call is only valid as long as no more objects are created before this point)
                            Console.WriteLine("Success! The managed V8NativeObject instance is disposed.");
                            Console.WriteLine("\r\nDone.\r\n");
                        }
                        else if (lcInput == @"\speedtest")
                        {
                            Console.Write("\r\nRunning the speed test ... ");
                            Console.Write("\r\n(Note: 'V8NativeObject' objects are always faster than using the 'V8ManagedObject' objects because native objects store values within the V8 engine and managed objects store theirs on the .NET side) \r\n");

                            var timer = new Stopwatch();
                            long startTime, elapsed;

                            _JSServer.WithContextScope = () =>
                            {
                                timer.Start();

                                var count = 200000000;

                                Console.WriteLine("\r\nTesting global property write speed ... ");
                                startTime = timer.ElapsedMilliseconds;
                                _JSServer.Execute("o={i:0}; for (o.i=0; o.i<" + count + "; o.i++) n = 0;"); // (o={i:0}; is used in case the global object is managed, which will greatly slow down the loop)
                                elapsed = timer.ElapsedMilliseconds - startTime;
                                var result1 = (double)elapsed / count;
                                Console.WriteLine(count + " loops @ " + elapsed + "ms total = " + result1.ToString("0.0#########") + " ms each pass.");

                                Console.WriteLine("\r\nTesting global property read speed ... ");
                                startTime = timer.ElapsedMilliseconds;
                                _JSServer.Execute("for (o.i=0; o.i<" + count + "; o.i++) n;");
                                elapsed = timer.ElapsedMilliseconds - startTime;
                                var result2 = (double)elapsed / count;
                                Console.WriteLine(count + " loops @ " + elapsed + "ms total = " + result2.ToString("0.0#########") + " ms each pass.");

                                count = 200000;

                                Console.WriteLine("\r\nTesting property write speed on a managed object (with interceptors) ... ");
                                _JSServer.DynamicGlobalObject.mo = _JSServer.CreateObjectTemplate().CreateObject();
                                startTime = timer.ElapsedMilliseconds;
                                _JSServer.Execute("o={i:0}; for (o.i=0; o.i<" + count + "; o.i++) mo.n = 0;");
                                elapsed = timer.ElapsedMilliseconds - startTime;
                                var result3 = (double)elapsed / count;
                                Console.WriteLine(count + " loops @ " + elapsed + "ms total = " + result3.ToString("0.0#########") + " ms each pass.");

                                Console.WriteLine("\r\nTesting property read speed on a managed object (with interceptors) ... ");
                                startTime = timer.ElapsedMilliseconds;
                                _JSServer.Execute("for (o.i=0; o.i<" + count + "; o.i++) mo.n;");
                                elapsed = timer.ElapsedMilliseconds - startTime;
                                var result4 = (double)elapsed / count;
                                Console.WriteLine(count + " loops @ " + elapsed + "ms total = " + result4.ToString("0.0#########") + " ms each pass.");

                                Console.WriteLine("\r\nUpdating native properties is {0:N2}x faster than managed ones.", result3 / result1);
                                Console.WriteLine("\r\nReading native properties is {0:N2}x faster than managed ones.", result4 / result2);
                            };
                            Console.WriteLine("\r\nDone.\r\n");
                        }
                        else if (lcInput == @"\exit")
                        {
                            Console.WriteLine("User aborted.");
                            break;
                        }
                        else if (lcInput.StartsWith(@"\"))
                        {
                            Console.WriteLine(@"Invalid console command. Type '\help' to see available commands.");
                        }
                        else
                        {
                            _JSServer.WithContextScope = () =>
                            {
                                Console.WriteLine();

                                try
                                {
                                    var result = _JSServer.Execute(input, "V8.NET Console");
                                    Console.WriteLine(result.AsString);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine();
                                    Console.WriteLine();
                                    Console.WriteLine(Exceptions.GetFullErrorMessage(ex));
                                    Console.WriteLine();
                                    Console.WriteLine("Error!  Press any key to continue ...");
                                    Console.ReadKey();
                                }
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine();
                        Console.WriteLine();
                        Console.WriteLine(Exceptions.GetFullErrorMessage(ex));
                        Console.WriteLine();
                        Console.WriteLine("Error!  Press any key to continue ...");
                        Console.ReadKey();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine();
                Console.WriteLine(Exceptions.GetFullErrorMessage(ex));
                Console.WriteLine();
                Console.WriteLine("Error!  Press any key to exit ...");
                Console.ReadKey();
            }
        }

        static void CurrentDomain_FirstChanceException(object sender, FirstChanceExceptionEventArgs e)
        {
        }

        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
        }
    }
}

[ScriptObject("Wrappable_Object")]
public class WrappableObject
{
    public int FieldA = 1;
    public string FieldB = "!!!";
    public int PropA { get { return FieldA; } }
    public string PropB { get { return FieldB; } }

    [ScriptMember("test", ScriptMemberSecurity.Locked)]
    public string Test(int a, string b) { FieldA = a; FieldB = b; return a + "_" + b; }

    [ScriptMember("testB", ScriptMemberSecurity.Locked)]
    public string Test(string b, int a) { FieldA = a; FieldB = b; return b + "_" + a; }
}

public class JS_Console : V8NativeObject
{
    public override void Initialize()
    {
        base.Initialize();
        Engine.GlobalObject.SetProperty("Console", Handle, V8PropertyAttributes.Locked);
        SetProperty("WriteLine", Engine.CreateFunctionTemplate("WriteLine").GetFunctionObject(WriteLine).Handle, V8PropertyAttributes.Locked);
    }

    public InternalHandle WriteLine(V8Engine engine, bool isConstructCall, InternalHandle _this, params InternalHandle[] args)
    {
        Console.WriteLine(String.Join("", args));
        return InternalHandle.Empty;
    }
}

/// <summary>
/// This is a custom implementation of 'V8Function' (which is not really necessary, but done as an example).
/// </summary>
public class V8DotNetTesterFunction : V8Function
{
    public override void Initialize()
    {
        Callback = ConstructV8DotNetTesterWrapper;
    }

    public InternalHandle ConstructV8DotNetTesterWrapper(V8Engine engine, bool isConstructCall, InternalHandle _this, params InternalHandle[] args)
    {
        return isConstructCall ? engine.GetObject<V8DotNetTesterWrapper>(_this) : InternalHandle.Empty;
        // (note: V8DotNetTesterWrapper would cause an error here if derived from V8ManagedObject)
    }
}

/// <summary>
/// When "new SomeType()"  is executed within JavaScript, the native V8 auto-generates objects that are not based on templates.  This means there is no way
/// (currently) to set interceptors to support IV8Object objects; However, 'V8NativeObject' objects are supported, so I'm simply creating a custom one here.
/// </summary>
public class V8DotNetTesterWrapper : V8NativeObject // (I can also implement IV8NativeObject instead here)
{
    V8DotNetTester _Tester;

    public override void Initialize()
    {
        _Tester = Engine.CreateObjectTemplate().CreateObject<V8DotNetTester>();
        SetProperty("tester", _Tester); // (or _Tester.Handle works also)
    }
}

public class V8DotNetTester : V8ManagedObject
{
    IV8Function _MyFunc;

    public override void Initialize()
    {
        base.Initialize();

        Console.WriteLine("\r\nInitializing V8DotNetTester ...\r\n");

        Console.WriteLine("Creating test property 1 (adding new JSProperty directly) ...");

        var myProperty1 = new JSProperty(Engine.CreateValue("Test property 1"));
        this.Properties.Add("testProperty1", myProperty1);

        Console.WriteLine("Creating test property 2 (adding new JSProperty using the IV8ManagedObject interface) ...");

        var myProperty2 = new JSProperty(Engine.CreateValue(true));
        ((IV8ManagedObject)this)["testProperty2"] = myProperty2;

        Console.WriteLine("Creating test property 3 (reusing JSProperty instance for property 1) ...");

        // Note: This effectively links property 3 to property 1, so they will both always have the same value, even if the value changes.
        this.Properties.Add("testProperty3", myProperty1); // (reuse a value)

        Console.WriteLine("Creating test property 4 (just creating a 'null' property which will be intercepted later) ...");

        this.Properties.Add("testProperty4", JSProperty.Empty);

        Console.WriteLine("Creating test property 5 (test the 'this' overload in V8ManagedObject, which will set/update property 5 without calling into V8) ...");

        this["testProperty5"] = Engine.CreateValue("Test property 5");

        Console.WriteLine("Creating test property 6 (using a dynamic property) ...");

        InternalHandle strValHandle = Engine.CreateValue("Test property 6");
        this.AsDynamic.testProperty6 = strValHandle;

        Console.WriteLine("Creating test function property 1 ...");

        var funcTemplate1 = Engine.CreateFunctionTemplate("_" + GetType().Name + "_");
        _MyFunc = funcTemplate1.GetFunctionObject(TestJSFunction1);
        this.AsDynamic.testFunction1 = _MyFunc;

        Console.WriteLine("\r\n... initialization complete.");
    }

    public void Execute()
    {
        Console.WriteLine("Begin testing properties on this.tester ...\r\n");

        // ... test the non-function/object propertied ...

        Engine.ConsoleExecute("assert('Testing property testProperty1', tester.testProperty1, 'Test property 1')", this.GetType().Name, true);
        Engine.ConsoleExecute("assert('Testing property testProperty2', tester.testProperty2, true)", this.GetType().Name, true);
        Engine.ConsoleExecute("assert('Testing property testProperty3', tester.testProperty3, tester.testProperty1)", this.GetType().Name, true);
        Engine.ConsoleExecute("assert('Testing property testProperty4', tester.testProperty4, '" + MyClassProperty4 + "')", this.GetType().Name, true);
        Engine.ConsoleExecute("assert('Testing property testProperty5', tester.testProperty5, 'Test property 5')", this.GetType().Name, true);
        Engine.ConsoleExecute("assert('Testing property testProperty6', tester.testProperty6, 'Test property 6')", this.GetType().Name, true);

        Console.WriteLine("\r\nAll properties initialized ok.  Testing property change ...\r\n");

        Engine.ConsoleExecute("assert('Setting testProperty2 to integer (123)', (tester.testProperty2=123), 123)", this.GetType().Name, true);
        Engine.ConsoleExecute("assert('Setting testProperty2 to number (1.2)', (tester.testProperty2=1.2), 1.2)", this.GetType().Name, true);

        // ... test non-function object properties ...

        Console.WriteLine("\r\nSetting property 1 to an object, which should also set property 3 to the same object ...\r\n");

        Engine.ConsoleExecute("tester.testProperty1 = {x:0}; assert('Testing property testProperty1.x === testProperty3.x', tester.testProperty1.x, tester.testProperty3.x)", this.GetType().Name, true);

        // ... test function properties ...

        Engine.ConsoleExecute("assert('Testing property tester.testFunction1 with argument 100', tester.testFunction1(100), 100)", this.GetType().Name, true);

        // ... test function properties ...

        Console.WriteLine("\r\nCreating 'this.obj1' with a new instance of tester.testFunction1 and testing the expected values ...\r\n");

        Engine.ConsoleExecute("obj1 = new tester.testFunction1(321);");
        Engine.ConsoleExecute("assert('Testing obj1.x', obj1.x, 321)", this.GetType().Name, true);
        Engine.ConsoleExecute("assert('Testing obj1.y', obj1.y, 0)", this.GetType().Name, true);
        Engine.ConsoleExecute("assert('Testing obj1[0]', obj1[0], 100)", this.GetType().Name, true);
        Engine.ConsoleExecute("assert('Testing obj1[1]', obj1[1], 100.2)", this.GetType().Name, true);
        Engine.ConsoleExecute("assert('Testing obj1[2]', obj1[2], '300')", this.GetType().Name, true);
        Engine.ConsoleExecute("assert('Testing obj1[3] is undefined?', obj1[3] === undefined, true)", this.GetType().Name, true);
        Engine.ConsoleExecute("assert('Testing obj1[4].toString()', obj1[4].toString(), 'Wed Jan 02 2013 03:04:05 GMT-0500 (Eastern Standard Time)')", this.GetType().Name, true);

        Console.WriteLine("\r\nPress any key to test dynamic handle property access ...\r\n");
        Console.ReadKey();

        // ... get a handle to an in-script only object and test the dynamic handle access ...

        Engine.ConsoleExecute("var obj = { x:0, y:0, o2:{ a:1, b:2, o3: { x:0 } } }", this.GetType().Name, true);
        dynamic handle = Engine.DynamicGlobalObject.obj;
        handle.x = 1;
        handle.y = 2;
        handle.o2.o3.x = 3;
        Engine.ConsoleExecute("assert('Testing obj.x', obj.x, 1)", this.GetType().Name, true);
        Engine.ConsoleExecute("assert('Testing obj.y', obj.y, 2)", this.GetType().Name, true);
        Engine.ConsoleExecute("assert('Testing obj.o2.o3.x', obj.o2.o3.x, 3)", this.GetType().Name, true);

        Console.WriteLine("\r\nPress any key to test handle reuse ...");
        Console.WriteLine("(1000 native object handles will be created, but one V8NativeObject wrapper will be used)");
        Console.ReadKey();
        Console.Write("Running ...");
        var obj = Engine.CreateObject(); // (need to create an object from a native object handle to begin with)
        for (var i = 0; i < 1000; i++)
        {
            obj.Handle = Engine.GlobalObject;
        }
        Console.WriteLine(" Done.");
    }

    public override InternalHandle NamedPropertyGetter(ref string propertyName)
    {
        if (propertyName == "testProperty4")
            return Engine.CreateValue(MyClassProperty4);

        return base.NamedPropertyGetter(ref propertyName);
    }

    public string MyClassProperty4 { get { return this.GetType().Name; } }

    public InternalHandle TestJSFunction1(V8Engine engine, bool isConstructCall, InternalHandle _this, params InternalHandle[] args)
    {
        // ... there can be two different returns based on the call mode! ...
        // (tip: if a new object is created and returned instead (such as V8ManagedObject or an object derived from it), then that object will be the new object (instead of "_this"))
        if (isConstructCall)
        {
            var obj = engine.GetObject(_this);
            obj.AsDynamic.x = args[0];
            ((dynamic)obj).y = 0; // (native objects in this case will always be V8NativeObject dynamic objects)
            obj.SetProperty(0, engine.CreateValue(100));
            obj.SetProperty("1", engine.CreateValue(100.2));
            obj.SetProperty("2", engine.CreateValue("300"));
            obj.SetProperty(4, engine.CreateValue(new DateTime(2013, 1, 2, 3, 4, 5)));
            return _this;
        }
        else return args.Length > 0 ? args[0] : InternalHandle.Empty;
    }
}

//!!public class __UsageExamplesScratchArea__ // (just here to help with writing examples for documentation, etc.)
//{
//    public void Examples()
//    {
//        var v8Engine = new V8Engine();

//        v8Engine.WithContextScope = () =>
//        {
//            // Example: Creating an instance.

//            var result = v8Engine.Execute("/* Some JavaScript Code Here */", "My V8.NET Console");
//            Console.WriteLine(result.AsString);
//            Console.WriteLine("Press any key to continue ...");
//            Console.ReadKey();

//            Handle handle = v8Engine.CreateInteger(0);
//            var handle = (Handle)v8Engine.CreateInteger(0);

//            var handle = v8Engine.CreateInteger(0);
//            // (... do something with it ...)
//            handle.Dispose();

//            // ... OR ...

//            using (var handle = v8Engine.CreateInteger(0))
//            {
//                // (... do something with it ...)
//            }

//            // ... OR ...

//            InternalHandle handle = InternalHandle.Empty;
//            try
//            {
//                handle = v8Engine.CreateInteger(0);
//                // (... do something with it ...)
//            }
//            finally { handle.Dispose(); }

//            handle.Set(anotherHandle);
//            // ... OR ...
//            var handle = anotherHandle.Clone(); // (note: this is only valid when initializing a variable)

//            var handle = v8Engine.CreateInteger(0);
//            var handle2 = handle;

//            handle.Set(anotherHandle.Clone());

//            // Example: Setting global properties.

//        };
//    }
//}