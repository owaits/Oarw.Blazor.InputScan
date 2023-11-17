
using Microsoft.AspNetCore.Components;

namespace Oarw.Blazor.InputScan
{
    public partial class ScanInstruction
    {
        [CascadingParameter]
        public InputScan? Parent { get; set; }

        [Parameter, EditorRequired]
        public string? Title { get; set; }

        [Parameter, EditorRequired]
        public string? Icon { get; set; }

        [Parameter]
        public string ButtonSelectedClass { get; set; } = "btn-primary";

        /// <summary>
        /// Gets or sets whether this is the default scan method to use.
        /// </summary>
        [Parameter]
        public bool Default { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether [single scan].
        /// </summary>
        [Parameter]
        public bool SingleScan { get; set; }

        /// <summary>
        /// Gets or sets whether the scan log is cleared when this option is selected.
        /// </summary>
        [Parameter]
        public bool ClearLog { get; set; } = false;

        [Parameter, EditorRequired]
        public Func<string, Task<object>>? OnScan { get; set; }

        [Parameter]
        public RenderFragment ChildContent { get; set; }

        /// <summary>
        /// Method invoked when the component is ready to start, having received its
        /// initial parameters from its parent in the render tree.
        /// Override this method if you will perform an asynchronous operation and
        /// want the component to refresh when that operation is completed.
        /// </summary>
        protected override async Task OnInitializedAsync()
        {
            Parent?.AddInstruction(this);
            await Task.CompletedTask;
        }

    }
}
