package com.example.smarthydro.data

// Replaces a list's contents in one step and drops any repeated key. At startup the cache is
// loaded from several places at once, and doing clear() then addAll() separately from each of
// them can interleave and leave the same item in the list twice, which crashes a list screen
// that uses the item's id as its key
fun <T, K> MutableList<T>.replaceWithUnique(items: List<T>, key: (T) -> K) {
    val unique = items.distinctBy(key)

    synchronized(this) {
        clear()
        addAll(unique)
    }
}
