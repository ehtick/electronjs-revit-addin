﻿using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ElectronJsRevitAddin.Application.ExternalEvents;
using ElectronJsRevitAddin.Application.Services;
using ElectronJsRevitAddin.Controllers;
using System;
using System.Diagnostics;
using System.IO;

namespace ElectronJsRevitAddin.ExternalCommands
{
	[Transaction(TransactionMode.Manual)]
	[Regeneration(RegenerationOption.Manual)]
	public class MainCommand : StartUp, IExternalCommand
	{

		/// <summary>
		/// Gets the main window handle.
		/// </summary>
		/// <value>
		/// The main window handle.
		/// </value>
		public static IntPtr MainWindowHandle { get; private set; }


		/// <summary>
		/// Gets the UI process.
		/// </summary>
		/// <value>
		/// The UI process.
		/// </value>
		public static Process UIProcess { get; set; }


		/// <summary>
		/// Executes the command.
		/// </summary>
		/// <param name="commandData">An ExternalCommandData object which contains reference to Application and View
		/// needed by external command.</param>
		/// <param name="message">Error message can be returned by external command. This will be displayed only if the command status
		/// was "Failed".  There is a limit of 1023 characters for this message; strings longer than this will be truncated.</param>
		/// <param name="elements">Element set indicating problem elements to display in the failure dialog.  This will be used
		/// only if the command status was "Failed".</param>
		/// <returns>The result indicates if the execution fails, succeeds, or was canceled by user. If it does not
		/// succeed, Revit will undo any changes made by the external command.</returns>
		public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
		{
			if (UIProcess == null || UIProcess.HasExited)
			{
				ConfigureServices();
				DocumentManager.Instance.Init(commandData.Application);
				ExternalExecutor.CreateExternalEvent();
				ControllerBase.InitServer();

				UIProcess = Process.Start(Path.GetDirectoryName(typeof(MainCommand).Assembly.Location) +
					"/electronjsrevitaddin.presentation/electronjsrevitaddin.presentation.exe");

				if (UIProcess.WaitForInputIdle(1000))
				{
					MainWindowHandle = UIProcess.MainWindowHandle;
					//WindowHandler.SetWindowOwner(commandData.Application, MainWindowHandle);
				}

			}

			return Result.Succeeded;
		}

	}

}