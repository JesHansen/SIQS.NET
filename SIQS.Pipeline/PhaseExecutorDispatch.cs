namespace SIQS.Pipeline;

/// <summary>Dispatches phase execution to one focused runner per phase.</summary>
public sealed class RealPhaseExecutor : IPhaseExecutor
{
    private readonly FactorBasePhaseRunner _factorBase;
    private readonly SievingPhaseRunner _sieving;
    private readonly FilteringPhaseRunner _filtering;
    private readonly LinearAlgebraPhaseRunner _linearAlgebra;
    private readonly SquareRootPhaseRunner _squareRoot;

    public RealPhaseExecutor()
    {
        _factorBase = new FactorBasePhaseRunner();
        _sieving = new SievingPhaseRunner();
        _filtering = new FilteringPhaseRunner();
        _linearAlgebra = new LinearAlgebraPhaseRunner();
        _squareRoot = new SquareRootPhaseRunner();
    }

    public Task<PhaseResult> RunFactorBaseAsync(PhaseContext context) => _factorBase.RunAsync(context);

    public Task<PhaseResult> RunSievingAsync(PhaseContext context) => _sieving.RunAsync(context);

    public Task<PhaseResult> RunFilteringAsync(PhaseContext context) => _filtering.RunAsync(context);

    public Task<PhaseResult> RunLinearAlgebraAsync(PhaseContext context) => _linearAlgebra.RunAsync(context);

    public Task<PhaseResult> RunSquareRootAsync(PhaseContext context) => _squareRoot.RunAsync(context);
}
