using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KeyNode
{
    internal class Dwarf : Exception
    {
        // DISCLAIMER: Dwarf Tossing is illegal in most countries, but it's okay in programming.
        public Dwarf(string message) : base(message)
        {
            ScreenMessages.PostScreenMessage($"[KeyNode] {message}");
        }
    }
}
