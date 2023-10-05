using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Pulumi;

public static class OtherExtensions
{
    
    public static Task<T> GetValue<T>(this Output<T> output) => output.GetValue(_ => _);
    public static TaskAwaiter<T> GetAwaiter<T>(this Output<T> output)
    {
        return output.GetValue().GetAwaiter();
    }

    public static Task<TResult> GetValue<T, TResult>(this Output<T> output, Func<T, TResult> valueResolver)
    {
        var tcs = new TaskCompletionSource<TResult>();
        output.Apply(_ =>
        {
            var result = valueResolver(_);
            tcs.SetResult(result);
            return result;
        });
        return tcs.Task;
    }
    public static Task<T> GetValue<T>(this Input<T> input) => input.GetValue(_ => _);
    public static TaskAwaiter<T> GetAwaiter<T>(this Input<T> input)
    {
        return input.GetValue().GetAwaiter();
    }

    public static Task<TResult> GetValue<T, TResult>(this Input<T> input, Func<T, TResult> valueResolver)
    {
        var tcs = new TaskCompletionSource<TResult>();
        input.Apply(_ =>
        {
            var result = valueResolver(_);
            tcs.SetResult(result);
            return result;
        });
        return tcs.Task;
    }
    
    public static Output<T> Delay<T>(this Output<T> output, int seconds)
    {
        return output.Apply(
            async x =>
            {
                await Task.Delay(seconds * 1000);
                return x;
            }
        );
    }

}