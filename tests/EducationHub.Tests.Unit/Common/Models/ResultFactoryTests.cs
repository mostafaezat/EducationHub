using EducationHub.Shared.Common.Models;

namespace EducationHub.Tests.Unit.Common.Models;

public class ResultFactoryTests
{
    // ResultFactory resolves the failure constructor by reflection against a closed generic
    // type. These tests exist because that failure mode is a runtime InvalidCastException,
    // not a compile error: nothing else in the build would catch it.
    [Fact]
    public void Failure_ForNonGenericResult_ReturnsFailedResultWithMessageAndErrors()
    {
        var errors = new[] { new Error("Tenant.Inactive", "inactive") };

        var result = ResultFactory.Failure<Result>("failed", errors);

        Assert.False(result.Succeeded);
        Assert.True(result.IsFailure);
        Assert.Equal("failed", result.Message);
        Assert.Equal(errors, result.Errors);
    }

    [Fact]
    public void Failure_ForGenericResult_PreservesTheConcreteGenericType()
    {
        var errors = new[] { new Error("Auth.ReuseDetected", "reused") };

        var result = ResultFactory.Failure<Result<int>>("failed", errors);

        Assert.IsType<Result<int>>(result);
        Assert.False(result.Succeeded);
        Assert.Equal("failed", result.Message);
        Assert.Equal(errors, result.Errors);
        Assert.Equal(0, result.Data);
    }

    [Fact]
    public void Failure_WithReferenceTypePayload_LeavesDataNull()
    {
        var result = ResultFactory.Failure<Result<string>>("failed");

        Assert.Null(result.Data);
        Assert.Single(result.Errors);
        Assert.Equal("failed", result.Errors[0].Message);
    }

    [Fact]
    public void Failure_WithCode_BuildsASingleErrorCarryingThatCode()
    {
        var result = ResultFactory.Failure<Result<Guid>>("Tenant.SubscriptionExpired", "expired");

        var error = Assert.Single(result.Errors);
        Assert.Equal("Tenant.SubscriptionExpired", error.Code);
        Assert.Equal("expired", error.Message);
    }

    [Fact]
    public void Failure_IsCachedPerClosedGenericType_AndStaysCorrectAcrossCalls()
    {
        var first = ResultFactory.Failure<Result<int>>("one");
        var second = ResultFactory.Failure<Result<int>>("two");

        Assert.Equal("one", first.Message);
        Assert.Equal("two", second.Message);
    }

    [Fact]
    public void GenericResult_DerivesFromResult_SoMediatRBehavioursCanConstrainOnIt()
    {
        // The inheritance is load-bearing: IPipelineBehavior is declared
        // "where TResponse : Result", and without it every generic handler would bypass
        // the pipeline behaviours entirely.
        Assert.True(typeof(Result).IsAssignableFrom(typeof(Result<int>)));
    }
}
