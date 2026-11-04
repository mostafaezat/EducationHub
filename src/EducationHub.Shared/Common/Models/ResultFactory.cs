using System.Reflection;

namespace EducationHub.Shared.Common.Models;

/// <summary>
/// Builds a failed result of a statically-known generic type.
/// </summary>
/// <remarks>
/// <para>
/// MediatR pipeline behaviours are declared as <c>where TResponse : Result</c>, so code
/// that catches an exception has to produce either a <see cref="Result"/> or a
/// <see cref="Result{T}"/> without knowing which. Doing that from a type parameter
/// requires reflection.
/// </para>
/// <para>
/// The obvious approach - <c>typeof(TTarget).GetMethod("Failure", ...)</c> - is fragile
/// here: the factories are declared <c>new</c> to hide their base counterparts, so
/// <c>GetMethod</c> can throw <c>AmbiguousMatchException</c>, and it breaks again if the
/// overloads are reordered. Looking up the <b>constructor</b> is unambiguous instead,
/// because <see cref="Result"/> and <see cref="Result{T}"/> differ in arity.
/// </para>
/// <para>
/// The cache is a static field on a generic class, so the CLR creates exactly one instance
/// per closed generic type: the reflection cost is paid once per response type for the
/// process lifetime, never once per request.
/// </para>
/// </remarks>
public static class ResultFactory
{
    /// <summary>Creates a failed <typeparamref name="TTarget"/> with the supplied message and errors.</summary>
    public static TTarget Failure<TTarget>(string message, IReadOnlyList<Error> errors)
        where TTarget : Result
        => (TTarget)FactoryCache<TTarget>.Create(message, errors);

    /// <summary>Creates a failed <typeparamref name="TTarget"/> with one error built from a stable code.</summary>
    public static TTarget Failure<TTarget>(string code, string message)
        where TTarget : Result
        => Failure<TTarget>(message, [new Error(code, message)]);

    /// <summary>
    /// Creates a failed <typeparamref name="TTarget"/> whose message is reused as the
    /// single error's message, under the generic error code <c>Error</c>.
    /// </summary>
    public static TTarget Failure<TTarget>(string message)
        where TTarget : Result
        => Failure<TTarget>(message, [new Error("Error", message)]);

    private static class FactoryCache<TTarget>
        where TTarget : Result
    {
        public static readonly Func<string, IReadOnlyList<Error>, Result> Create = Build();

        private static Func<string, IReadOnlyList<Error>, Result> Build()
        {
            // Result    : (bool succeeded, string message, IReadOnlyList<Error> errors)
            // Result<T> : (bool succeeded, T? data, string message, IReadOnlyList<Error> errors)
            var match = typeof(TTarget)
                .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Select(c => (Constructor: c, Parameters: c.GetParameters()))
                .FirstOrDefault(x =>
                    x.Parameters is { Length: 3 or 4 }
                    && x.Parameters[0].ParameterType == typeof(bool)
                    && x.Parameters[^1].ParameterType == typeof(IReadOnlyList<Error>)
                    && x.Parameters[^2].ParameterType == typeof(string));

            if (match.Constructor is null)
            {
                throw new InvalidOperationException(
                    $"'{typeof(TTarget)}' exposes no compatible failure constructor. " +
                    "Result types must keep the (bool succeeded, [T? data,] string message, " +
                    "IReadOnlyList<Error> errors) constructor shape.");
            }

            var takesData = match.Parameters.Length == 4;
            var dataType = takesData ? match.Parameters[1].ParameterType : null;

            return (message, errors) =>
            {
                object?[] arguments = takesData
                    ? [false, dataType!.IsValueType ? Activator.CreateInstance(dataType) : null, message, errors]
                    : [false, message, errors];

                return (Result)match.Constructor.Invoke(arguments);
            };
        }
    }
}
