﻿//*********************************************************************
//CAD+ Toolset
//Copyright(C) 2020 Xarial Pty Limited
//Product URL: https://cadplus.xarial.com
//License: https://cadplus.xarial.com/license/
//*********************************************************************

using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Navigation;
using System.Windows.Threading;
using Xarial.CadPlus.Module.Init;

namespace Xarial.CadPlus.Common
{
    internal static class ConsoleHandler
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetStdHandle(int nStdHandle, IntPtr handle);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int GetFileType(IntPtr handle);

        private const int FILE_TYPE_DISK = 0x0001;
        private const int FILE_TYPE_PIPE = 0x0003;

        private const int STD_OUTPUT_HANDLE = -11;
        private const int STD_ERROR_HANDLE = -12;
        
        internal static void Attach()
        {
            //need to call before AttachConsoel so the output can be redirected
            var outHandle = GetStdHandle(STD_OUTPUT_HANDLE);

            if (IsOutputRedirected(outHandle))
            {
                var outWriter = Console.Out;
            }

            bool errorRedirected = IsOutputRedirected(GetStdHandle(STD_ERROR_HANDLE));

            if (errorRedirected) 
            {
                var errWriter = Console.Error;
            }

            AttachConsole(-1);

            if (!errorRedirected)
            {
                SetStdHandle(STD_ERROR_HANDLE, outHandle);
            }
        }

        private static bool IsOutputRedirected(IntPtr handle)
        {
            var fileType = GetFileType(handle);

            return fileType == FILE_TYPE_DISK || fileType == FILE_TYPE_PIPE;
        }
    }

    public class ConsoleHostModule : BaseHostModule
    {
        public override IntPtr ParentWindow => IntPtr.Zero;

        public override event Action Loaded;

        internal ConsoleHostModule() 
        {
            Loaded?.Invoke();
        }
    }

    public class WpfAppHostModule : BaseHostModule
    {
        private readonly Application m_App;

        internal WpfAppHostModule(Application app)
        {
            m_App = app;
            m_App.Activated += OnAppActivated;
        }

        public override IntPtr ParentWindow => m_App.MainWindow != null
            ? new WindowInteropHelper(m_App.MainWindow).Handle
            : IntPtr.Zero;

        public override event Action Loaded;

        private void OnAppActivated(object sender, EventArgs e)
        {
            m_App.Activated -= OnAppActivated;
            Loaded?.Invoke();
        }
    }

    public abstract class MixedApplication<TCliArgs> : Application
    {
        private bool m_IsStartWindowCalled;

        private BaseHostModule m_HostModule;

        protected virtual void OnAppStart()
        {
        }

        protected virtual void TryExtractCliArguments(Parser parser, string[] input, 
            out TCliArgs args, out bool hasArguments, out bool hasError)
        {
            args = default;
            hasError = false;
            hasArguments = false;

            if (input.Any())
            {
                TCliArgs argsLocal = default;
                bool hasErrorLocal = false;

                parser.ParseArguments<TCliArgs>(input)
                    .WithParsed(a => argsLocal = a)
                    .WithNotParsed(err => hasErrorLocal = true);

                args = argsLocal;
                hasError = hasErrorLocal;
                hasArguments = true;
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            this.DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            OnAppStart();

            var parserOutput = new StringBuilder();

            TCliArgs args;
            bool hasArgs;
            bool hasError;

            using (var outputWriter = new StringWriter(parserOutput))
            {
                var parser = new Parser(p =>
                {
                    p.CaseInsensitiveEnumValues = true;
                    p.AutoHelp = true;
                    p.EnableDashDash = true;
                    p.HelpWriter = outputWriter;
                    p.IgnoreUnknownArguments = false;
                });

                TryExtractCliArguments(parser, e.Args, out args, out hasArgs, out hasError);
            }

            if (hasArgs)
            {
                //WindowsApi.AttachConsole(-1);
                ConsoleHandler.Attach();

                SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
                m_HostModule = new ConsoleHostModule();
                
                var res = false;

                if (!hasError)
                {
                    try
                    {
                        RunConsole(args)
                            .ConfigureAwait(false)
                            .GetAwaiter()
                            .GetResult();
                        res = true;
                    }
                    catch (Exception ex)
                    {
                        //TODO: message exception
                        PrintError(ex.Message);
                    }
                }
                else 
                {
                    Console.Write(parserOutput.ToString());
                }

                Environment.Exit(res ? 0 : 1);
            }
            else
            {
                m_HostModule = new WpfAppHostModule(this);
                base.OnStartup(e);
            }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            if (!m_IsStartWindowCalled)
            {
                m_IsStartWindowCalled = true;
                OnWindowStarted();
            }
        }

        protected virtual void OnWindowStarted() 
        {
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            //TODO: log
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            //TODO: log
            e.Handled = true;
        }

        protected abstract Task RunConsole(TCliArgs args);

        private void PrintError(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(msg);
            Console.ResetColor();
        }
    }
}
