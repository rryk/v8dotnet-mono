using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace V8.Net.Tests
{
    [TestClass]
    public class V8EngineTests
    {
        [TestMethod]
        public void TestV8EngineInstantiation()
        {
            var v8Engine = new V8Engine();

            v8Engine.WithContextScope = () =>
            {
                var result = v8Engine.Execute("x = 0;", "V8.NET Unit Tester");
            };
        }

        [TestMethod]
        public void TestGlobalPropertyGetAndSet()
        {
            var v8Engine = new V8Engine();

            v8Engine.WithContextScope = () =>
            {
                v8Engine.DynamicGlobalObject.x = 0;
                Assert.AreEqual<int>((int)v8Engine.DynamicGlobalObject.x, 0); // (this should trigger a call to V8Engine.CreateNativeValue(), so test for property handle creation)
            };
        }

        [TestMethod]
        public void TestIndexedObjectList()
        {
            var indexedObjectList = new IndexedObjectList<string>();

            indexedObjectList.Add("Test1");

            Assert.AreEqual<string>(indexedObjectList[0], "Test1");
        }
    }
}
