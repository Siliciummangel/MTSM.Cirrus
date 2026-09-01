using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MTSM.Cirrus.API.Filters;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        RemoveValueProviderFactory<FormValueProviderFactory>(context);
        RemoveValueProviderFactory<FormFileValueProviderFactory>(context);
        RemoveValueProviderFactory<JQueryFormValueProviderFactory>(context);
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }

    private static void RemoveValueProviderFactory<T>(
        ResourceExecutingContext context)
        where T : IValueProviderFactory
    {
        IValueProviderFactory? factory = context.ValueProviderFactories
            .FirstOrDefault(item => item is T);

        if (factory is not null)
        {
            context.ValueProviderFactories.Remove(factory);
        }
    }
}
