using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

#if V2 || V3 || V3_5
#else
using System.Dynamic;
#endif

namespace V8.Net
{
    // ========================================================================================================================
    // The handles section has methods to deal with creating and disposing of managed handles (which wrap native V8 handles).
    // This helps to reuse existing handles to prevent having to create new ones every time, thus greatly speeding things up.

    public unsafe partial class V8Engine
    {
        // --------------------------------------------------------------------------------------------------------------------

        internal Thread _Worker;

        /// <summary>
        /// When handle proxies are no longer in use, they are registered here for quick reference so the worker thread can dispose of them.
        /// <para>Note: If a managed object is associated, all references to it must also be gone before a handle can be disposed.</para>
        /// </summary>
        internal readonly List<int> _ObjectInfosToBeMadeWeak = new List<int>(100);

        // --------------------------------------------------------------------------------------------------------------------

        void _Initialize_Worker()
        {
            _Worker = new Thread(_WorkerLoop) { IsBackground = true }; // (note: must set 'IsBackground=true', else the app will hang on exit)
            _Worker.Priority = ThreadPriority.Lowest;
            _Worker.Start();
        }

        // --------------------------------------------------------------------------------------------------------------------

        volatile int _PauseWorker;

        void _WorkerLoop()
        {
            bool workPending;

            while (true)
            {
                if (_PauseWorker == 1) _PauseWorker = 2;
                else
                {
                    workPending = _ObjectInfosToBeMadeWeak.Count > 0;

                    while (workPending && _PauseWorker == 0)
                    {
                        WithIsolateScope = () =>
                        {
                            workPending = _DoWorkStep();
                            DoIdleNotification(1);
                        };
                        Thread.Sleep(0);
                    }
                }
                Thread.Sleep(100);
                WithIsolateScope = () =>
                {
                    DoIdleNotification(100);
                };
            }
        }

        int _Worker_Index = -1;

        /// <summary>
        /// Does one step in the work process (mostly garbage collection for freeing up unused handles).
        /// True is returned if more work is pending, and false otherwise.
        /// </summary>
        bool _DoWorkStep()
        {
            int objInfoID;
            _ObjectInfo objInfo;

            lock (_ObjectInfosToBeMadeWeak)
            {
                if (_Worker_Index < 0)
                    _Worker_Index = _ObjectInfosToBeMadeWeak.Count - 1;

                if (_Worker_Index >= 0)
                {
                    objInfoID = _ObjectInfosToBeMadeWeak[_Worker_Index];
                    objInfo = _Objects[objInfoID];
                    objInfo._MakeWeak();
                    _ObjectInfosToBeMadeWeak.RemoveAt(_Worker_Index);

                    _Worker_Index--;
                }

                return _Worker_Index >= 0;
            }
        }

        /// <summary>
        /// Pauses the worker thread (usually for debug purposes). The worker thread clears out orphaned object entries (mainly).
        /// </summary>
        public void PauseWorker()
        {
            _PauseWorker = 1;
            while (_PauseWorker == 1) { }
        }

        /// <summary>
        /// Unpauses the worker thread (see <see cref="PauseWorker"/>).
        /// </summary>
        public void ResumeWorker()
        {
            _PauseWorker = 0;
        }

        // --------------------------------------------------------------------------------------------------------------------
    }

    // ========================================================================================================================
}
