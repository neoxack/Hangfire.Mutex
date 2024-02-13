Hangfire.Mutex
=======

Mutex prevents concurrent execution of multiple background jobs that share the same resource identifier. All we need is to decorate our background job methods with the MutexAttribute filter and define what resource identifier should be used.

```
[Mutex("my-resource")]
public void MyMethod()
{
    // ...
}
```

We can also create multiple background job methods that share the same resource identifier, and mutual exclusive behavior will span all of them, regardless of the method name.

```
[Mutex("my-resource")]
public void FirstMethod() { /* ... */ }

[Mutex("my-resource")]
public void SecondMethod() { /* ... */ }
```

Since mutexes are created dynamically, it’s possible to use a dynamic resource identifier based on background job arguments. To define it, we should use String.Format-like templates, and during invocation all the placeholders will be replaced with actual job arguments. But ensure everything is lower-cased and contains only alphanumeric characters with limited punctuation – no rules except maximum length and case insensitivity is enforced, but it’s better to keep identifiers simple.

```
[Mutex("orders:{0}")]
public void ProcessOrder(long orderId) { /* ... */ }

[Mutex("newsletters:{0}:{1}")]
public void ProcessNewsletter(int tenantId, long newsletterId) { /* ... */ }
```
