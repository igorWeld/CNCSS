using CNCSS.Controller.Core;
using CNCSS.Logic;
using CNCSS.Logic.NcPrograms;
using CNCSS.Machine.Configuration;
using CNCSS.Machine.Core;
using CNCSS.Simulation.Bus;
using CNCSS.Simulation.Execution;
using CNCSS.UI.Hosts;
using CNCSS.UI.Presenters;
using CNCSS.UI.Views;
using CNCSS.Vis;

namespace CNCSS.Infrastructure.Composition
{
    /// <summary>Application composition root: owns construction of core services and presenters.</summary>
    public sealed class AppCompositionRoot
    {
        public AppCompositionRoot()
        {
            SimulationBus = new SimulationBus();
            ControllerCore = new ControllerCore(SimulationBus);
            MachineCore = new MachineCore(SimulationBus);
            ProgramExecutionService = new ProgramExecutionService(SimulationBus);
            PlaybackLoopService = new PlaybackLoopService(ProgramExecutionService);
            ProgramLoader = new ProgramLoader();
            ProgramStateService = new ProgramStateService();
            ProgramWorkspace = new ProgramWorkspace(ProgramLoader, ProgramStateService);
            CycleCoordinator = new CycleCoordinator(ControllerCore, ProgramExecutionService, PlaybackLoopService);
            StockCoordinator = new StockSimulationCoordinator();
            ProgramPlaybackHost = new ProgramPlaybackHost(CycleCoordinator, PlaybackLoopService, ProgramExecutionService, MachineCore);
            UiRenderService = new UiRenderService();
            StockRenderService = new StockRenderService();
            ToolpathRenderService = new ToolpathRenderService();
            ToolpathSceneBuilder = new ToolpathSceneBuilder();
            StockLifecycleCoordinator = new StockLifecycleCoordinator(StockCoordinator);
            ProgramLoadOrchestrator = new ProgramLoadOrchestrator(ProgramWorkspace, ProgramExecutionService, ProgramPlaybackHost);
            NcProgramCatalogService = new NcProgramCatalogService();
            MachineProfileStore = new MachineProfileStore();
            MachineProfileService = new MachineProfileService(MachineProfileStore);
            StlMeshLoader = new StlMeshLoader();
            MachineVisualCoordinator = new MachineVisualCoordinator(MachineProfileService, StlMeshLoader);
        }

        public ISimulationBus SimulationBus { get; }
        public IControllerCore ControllerCore { get; }
        public IMachineCore MachineCore { get; }
        public ProgramExecutionService ProgramExecutionService { get; }
        public PlaybackLoopService PlaybackLoopService { get; }
        public ProgramLoader ProgramLoader { get; }
        public ProgramStateService ProgramStateService { get; }
        public ProgramWorkspace ProgramWorkspace { get; }
        public CycleCoordinator CycleCoordinator { get; }
        public StockSimulationCoordinator StockCoordinator { get; }
        public StockLifecycleCoordinator StockLifecycleCoordinator { get; }
        public ProgramPlaybackHost ProgramPlaybackHost { get; }
        public UiRenderService UiRenderService { get; }
        public StockRenderService StockRenderService { get; }
        public ToolpathRenderService ToolpathRenderService { get; }
        public ToolpathSceneBuilder ToolpathSceneBuilder { get; }
        public ProgramLoadOrchestrator ProgramLoadOrchestrator { get; }
        public NcProgramCatalogService NcProgramCatalogService { get; }
        public MachineProfileStore MachineProfileStore { get; }
        public MachineProfileService MachineProfileService { get; }
        public StlMeshLoader StlMeshLoader { get; }
        public MachineVisualCoordinator MachineVisualCoordinator { get; }

        public MainPresenter CreateMainPresenter(IMainView view)
        {
            return new MainPresenter(view, ProgramLoadOrchestrator, CycleCoordinator);
        }
    }
}
