---
title: "Code Generation"
icon: "🧞"
created: 2023-11-19
updated: 2025-06-15
---

# Code Generation

# Capabilities

s&box has a `[CodeGenerator]` attribute that you can use to decorate another attribute specifically for use with methods and properties. It lets you wrap methods and properties to perform some other action when the method is called or the property is set or to return a different value when the property is read.

# Examples

[The Scene System] uses CodeGen for Broadcast RPCs, it could also be used for creating networked variables.

## RPCs


:::info
The following is an example of how you could use `CodeGenerator` to create an RPC system.

:::

```csharp
[CodeGenerator( CodeGeneratorFlags.WrapMethod | CodeGeneratorFlags.Instance, "OnRPCInvoked" )]
public class RPC : Attribute {}

public class MyObject
{
  [RPC]
  public void SendMessage( string message )
  {
    Log.Info( message );
  }
  
  internal void OnRPCInvoked( WrappedMethod m, params object[] args )
  {
    if ( IsServer )
    {
      // Send a networked message with the specified args and method name to all clients.
    }
    
    // Call the original method.
    m.Resume();
  }
}
```

## Networked Vars


:::info
This is an example of how you could use `CodeGenerator` to create a system for networked variables.

:::

```csharp
[CodeGenerator( CodeGeneratorFlags.WrapPropertySet | CodeGeneratorFlags.Instance, "OnNetVarSet" )]
[CodeGenerator( CodeGeneratorFlags.WrapPropertyGet | CodeGeneratorFlags.Instance, "OnNetVarGet" )]
public class NetVar : Attribute {}

public class MyObject
{
  [NetVar] public string Name { get; set; }
  
  internal T OnWrapGet<T>( WrappedPropertyGet<T> p)
  {
    // Return the actual value from the network.
    if ( MyNetVarTable.TryGetValue( p.PropertyName, out var netValue ) )
    {
      return (T)netValue;
    }
    
    return val;
  }

  internal void OnWrapSet<T>( WrappedPropertySet<T> p )
  {
    if ( IsServer )
    {
      MyNetVarTable[propertyName] = p.Value;
      // Send a networked message setting the property to this value for all clients.
    }
    
    p.Setter( p.Value);
  }
}
```


# Code Generator Flags

The `CodeGenerator` attribute needs to have `CodeGeneratorFlags` set which determine what it will wrap and whether the wrapping applies to static methods and properties or instance methods and properties or both.
nAn attribute class can be decorated with more than one `CodeGenerator` attribute to handle many different scenarios.


# Wrapping Methods

To simply wrap a method call you can create an attribute that is decorated with `CodeGenerator` and the `CodeGeneratorFlags.WrapMethod` flag. To create one that wraps both instance and static methods you can use something like this:

```csharp
[AttributeUsage( AttributeTargets.Method )]
[CodeGenerator( CodeGeneratorFlags.WrapMethod | CodeGeneratorFlags.Instance, "OnMethodInvoked" )]
[CodeGenerator( CodeGeneratorFlags.WrapMethod | CodeGeneratorFlags.Static,
	"MyObject.OnMethodInvokedStatic" )]
public class WrapCall : Attribute {}
```



:::info
The passed `callbackName` to the `CodeGenerator` attribute can either be a static method (determined by whether there's a `.` in the string) or an instance method. You can only use an instance method if the wrapped method itself is an instance method.

:::


You can then setup those target methods on your object or static class like this:

```csharp
public class MyObject
{
	internal static void OnMethodInvokedStatic( WrappedMethod m, params object[] args ) {}
	internal void OnMethodInvoked( WrappedMethod m, params object[] args ) {}
}
```


:::info
`methodName` on a static callback will be the fully qualified name. For example if `[WrapCall]` was added to a method called `DoSomething` on `MyClass` then the method name would be `MyClass.DoSomething`.

:::

## Different Parameter Types

You can handle different parameter types instead of having a single generic callback signature. The correct method will be called based on the original parameters of the wrapped method. You can even use generics here.

```csharp
public class MyObject
{
	internal static void OnMethodInvokedStatic<T1, T2>( WrappedMethod m, T1 arg1, T2 arg2 ) {}
	
	internal void OnMethodInvoked( WrappedMethod m, bool enabled ) {}
	internal void OnMethodInvoked<T1, T2, T3>( WrappedMethod m, T1 arg1, T2 arg3, T3 arg3 ) {}
}
```

## Different Return Types

If you want to handle specific return types you can also do that. The crucial part is that instead of the first parameter of the callback method being an `Action` it would be a `Func<T>` instead.

```csharp
public class MyObject
{
	internal T OnMethodInvoked( WrappedMethod<T> m )
	{
		return m.Resume();
	}
}
```


# Wrapping Properties

Wrapping properties is similar to wrapping a method, but your attribute class should use `CodeGeneratorFlags.WrapPropertySet` and/or `CodeGeneratorFlags.WrapPropertyGet`.

```csharp
[AttributeUsage( AttributeTargets.Property )]
[CodeGenerator( CodeGeneratorFlags.WrapPropertySet | CodeGeneratorFlags.Instance, "OnWrapSet" )]
[CodeGenerator( CodeGeneratorFlags.WrapPropertyGet | CodeGeneratorFlags.Instance, "OnWrapGet" )]
public class WrapGetSet : Attribute {}
```



:::tip
Similarly to wrapping methods, the callback method can handle any generic property or specific property types.

:::


When wrapping the setter of properties the callback method should have 3 parameters. The first is the property name, the second is the value that the property *wants* to be set to, and the third is an `Action` that will call the original setter function.

```csharp
public void OnWrapSet<T>( WrappedPropertySet<T> p )
{
	p.Setter( p.Value );
}
```


When wrapping the getter of properties, the callback method should have a return type and 2 parameters. The first being the property name and the second being the value that the getter *would have* returned usually.

```csharp
public T OnWrapGet<T>( WrappedPropertyGet<T> p )
{
	return p.Value;
}
```


# Wrapping Everything

To demonstrate how you can mix `CodeGeneratorFlags` to handle multiple use cases, here is an example of an attribute that could wrap anything and everything.



:::warning
Because we specify `CodeGeneratorFlags.Static` in this attribute, the `callbackName` must refer to a **static** method, too.

:::


```csharp
[CodeGenerator(
	CodeGeneratorFlags.WrapPropertySet | CodeGeneratorFlags.WrapPropertyGet | CodeGeneratorFlags.WrapMethod |
	CodeGeneratorFlags.Static | CodeGeneratorFlags.Instance, "MyStaticClass.OnWrapAnything" )]
public class WrapAnything : Attribute {}

public class MyObject
{
  [WrapAnything] public string MyString { get; set; }
  [WrapAnything] public static string MyStaticString { get; set; }
  
  [WrapAnything]
  public void MyMethod()
  {
  }
  
  [WrapAnything]
  public static void MyStaticMethod()
  {
  }
}

public static class MyStaticClass
{
  internal static void OnWrapAnything<T>( WrappedPropertySet<T> p )
  {
  }

  internal static T OnWrapAnything<T>( WrappedPropertyGet<T> p )
  {
    return value;
  }

  internal static void OnWrapAnything( WrappedMethod m, params object[] args )
  {
    m.Resume();
  }

  internal static T OnWrapAnything( WrappedMethod<T> p, params object[] args )
  {
    m.Resume();
  }
}
```
