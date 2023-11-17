using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Oarw.Blazor.InputScan
{
    public partial class Audio : IAsyncDisposable
    {
        private AudioJsInterop? audioJs;

        [Inject]
        public IJSRuntime? JS { get; set; }

        public Guid Id { get; set; } = Guid.NewGuid();

        [Parameter, EditorRequired]
        public string? Source { get; set; }

        protected override async Task OnInitializedAsync()
        {
            if(JS != null)
            {
                audioJs = new AudioJsInterop(JS);
            }
            
            await Task.CompletedTask;
        }

        public async Task Play()
        {
            if(Source != null && audioJs != null)
            {
                Console.WriteLine($"Sound: {Id}");
                await audioJs.PlayAudio(Id.ToString());
            }
        }
        public async ValueTask DisposeAsync()
        {
            if(audioJs != null)
            {
                await audioJs.DisposeAsync();
                audioJs = null;
            }            
        }
    }
}
