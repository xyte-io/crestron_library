using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Crestron.SimplSharp;

namespace SyncProLibrary
{
    public class SimplPlusDataClasses
    {
        #region Basic stractures for S+
        /// <summary>
        /// This is a basic stracture that can be treasnferred to S+
        /// </summary>
        public class AppsObject
        {
            public UiApp[] apps { get; set; }
            public short numberOfApps { get; set; }

            //Must have default constructor. Otherwise S+ doesn't recognize the object
            public AppsObject() { }

            /// <summary>
            /// Basic constructor from list of apps
            /// </summary>
            /// <param name="list"></param>
            public AppsObject(List<UiApp> list)
            {
                numberOfApps = (short)list.Count;
                apps = list.ToArray();
            }
        }

        public class ActionableButtonsObject
        {
            public UiActionableButton[] buttons { get; set; }
            public short numberOfButtons { get; set; }

            //Must have default constructor. Otherwise S+ doesn't recognize the object
            public ActionableButtonsObject() { }
            public ActionableButtonsObject(List<UiActionableButton> list)
            {
                numberOfButtons = (short)list.Count;
                buttons = list.ToArray();
            }
        }
        #endregion
    }
}