using MelonLoader;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;


namespace RiccaMod.Patches
{
    internal interface IPatch
    {
        string Name { get; }
        string Description { get; }
        bool Enabled { get; }
        void LoadSettings(MelonPreferences_Category cat);
        void Patch();
        void Unpatch();
    }

    
}
