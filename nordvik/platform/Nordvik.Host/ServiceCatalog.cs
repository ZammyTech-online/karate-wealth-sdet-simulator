using Nordvik.Services;
using Nordvik.Shared;

namespace Nordvik.Host;

/// <summary>Todos los microservicios que este anfitrión sabe arrancar. Cada oleada añade los suyos aquí.</summary>
public static class ServiceCatalog
{
    public static IReadOnlyList<INordvikService> Create() => new INordvikService[]
    {
        // ---------- Oleada 0 ----------
        new UuidService(), new SequenceService(), new ClockService(), new RandomSeedService(),
        new ProductCatalog(), new MarketData(), new FxRates(), new CalendarService(),
        // ---------- Oleada 1 ----------
        new SyntheticPersonService(), new AddressService(), new IbanService(), new IsinService(), new DataResetService(), new FixtureLoader(),
        new ApiGateway(), new RateLimiter(), new ConfigService(),
        new IamService(), new SessionService(), new PermissionService(),
        new CustomerRegistry(), new ContactService(), new CustomerAddressService(), new PreferencesService(), new CustomerSearch(),
        new FundFactsheet(), new FeeEngine(), new BenchmarkService(),
        new PortfolioService(), new AllocationEngine(),
        // ---------- Oleada 2 ----------
        new EventBus(), new OutboxRelay(), new AuditService(), new IdempotencyStore(), new HashService(), new CorrelationTracer(),
        new EidBroker(), new MfaService(), new StepUpService(), new MessageTemplateService(), new NotificationService(),
        new CustodyAdapter(), new PriceFeedAdapter(), new NordPayAdapter(),
        new LedgerService(), new CashManagement(), new BankAccountVerification(), new PaymentInstructions(), new PaymentsGateway(),
        new RefundsReturns(), new GlExport(), new ReconciliationService(), new FeeBilling(),
        new OrderManagement(), new OrderRouter(), new ExecutionService(), new PositionKeeping(), new ValuationService(), new PerformanceService(),
        new RebalancingEngine(), new CorporateActions(),
        new ContractService(), new ContractTemplateService(), new ClauseEngine(), new SigningService(), new DocumentVault(), new RenderService(), new AntivirusScan(),
        new TextFactory(), new FileFactory(),
        new CompositionService(), new BffWeb(), new BffMobile(), new BffAdvisor(), new OnboardingOrchestrator(),
        // ---------- Oleada 3 ----------
        new KeyManagement(), new PiiVault(), new ConsentLedger(), new DataPassport(), new LineageService(), new RedactionService(), new RetentionScheduler(),
        new ChaosControl(), new MockControl(), new EventSpy(), new JwtForge(), new SecretsService(), new TokenExchange(),
        new KycAmlService(), new SanctionsAdapter(), new PepService(), new RiskScoring(), new TransactionMonitoring(), new SuitabilityService(), new AppropriatenessService(),
        new HouseholdService(), new EmploymentService(), new TaxResidencyService(), new CaseManagement(), new TaskQueue(), new ApprovalService(), new PopulationAdapter(),
        // ---------- Sistemas externos simulados (no cuentan entre los 156: son "el mundo exterior") ----------
        new NordIdSim(), new PopulationSim(), new SanctionScreenSim(), new CustodianSim(), new PriceFeedSim(), new NordPaySim(),
    };

    /// <summary>Un servicio y todo aquello de lo que depende, recursivamente.</summary>
    public static IReadOnlyList<string> WithDependencies(IEnumerable<string> names, IReadOnlyDictionary<string, INordvikService> all)
    {
        var result = new List<string>();
        void Visit(string n)
        {
            if (result.Contains(n) || !all.ContainsKey(n)) return;
            result.Add(n);
            foreach (var d in all[n].Info.DependsOn ?? Array.Empty<string>()) Visit(d);
        }
        foreach (var n in names) Visit(n);
        if (!result.Contains("clock-service")) result.Add("clock-service");
        return result;
    }
}
