// Global using directives for the Agent/ namespace tree.
// Scoped to Agent namespace only — avoids polluting existing WPF/WinForms code
// which already has its own using directives and cannot tolerate ambiguity
// between System.Windows.Controls and System.Windows.Forms.

global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.Threading;
global using System.Threading.Tasks;
